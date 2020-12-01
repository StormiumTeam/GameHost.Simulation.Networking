using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Execution;
using GameHost.Game;
using GameHost.Injection;
using GameHost.Revolution.NetCode.LLAPI;
using GameHost.Revolution.NetCode.LLAPI.Systems;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.Application;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;
using GameHost.Threading;
using GameHost.Threading.Apps;
using GameHost.Transports;
using GameHost.Worlds.Components;
using JetBrains.Annotations;
using NUnit.Framework;

namespace GameHost.Revolution.NetCode.Tests
{
	public class ApplicationTest
	{
		private GameBootstrap      gameBootstrap;
		private IApplication       serverApp;
		private IApplication       clientApp;
		private ListenerCollection listenerCollection;

		[SetUp]
		public void Setup()
		{
			gameBootstrap = new GameBootstrap();
			gameBootstrap.GameEntity.Set(new GameName("Tests.ApplicationTest"));
			gameBootstrap.Setup();

			serverApp = new CommonApplicationThreadListener(gameBootstrap.Global, null);
			clientApp = new CommonApplicationThreadListener(gameBootstrap.Global, null);

			listenerCollection = new ListenerCollection();
			listenerCollection.AddListener((IListener) serverApp);
			listenerCollection.AddListener((IListener) clientApp);
		}

		[Test]
		public void CreateServerClientConnection()
		{
			var serverDriver = new ThreadTransportDriver(1);
			serverDriver.Listen();

			var worldTime = new ManagedWorldTime {Delta = TimeSpan.FromSeconds(0.1)};
			foreach (var obj in listenerCollection.RetrieveAll(null))
			{
				if (obj is not CommonApplicationThreadListener app)
					throw new InvalidOperationException();

				IFeature feature = app switch
				{
					{ } listener when listener == serverApp => new ServerFeature(serverDriver, default),
					{ } listener when listener == clientApp => new ClientFeature(serverDriver.TransportAddress.Connect(), default),
					_ => throw new InvalidOperationException(),
				};
				
				app.Data.Context.BindExisting(new GameWorld());
				app.Data.Context.BindExisting<IManagedWorldTime>(worldTime);
				app.Data.Context.BindExisting<IApplication>(app);
				
				app.Data.World.CreateEntity().Set(feature);
				app.Data.Collection.GetOrCreate(wc => new AddComponentsClientFeature(wc));
				app.Data.Collection.GetOrCreate(wc => new AddComponentsServerFeature(wc));
				app.Data.Collection.GetOrCreate(wc => new UpdateDriverSystem(wc));
				app.Data.Collection.GetOrCreate(wc => new SendSnapshotSystem(wc));
				app.Data.Collection.GetOrCreate(wc => new SendSystems(wc));
				app.Data.Collection.GetOrCreate(wc => new SerializerCollection(wc));
				app.Data.Collection.GetOrCreate(wc => new TestCreateSystem(wc));
			}

			for (var i = 0; i != 25; i++)
			{
				if (!gameBootstrap.Loop())
					break;

				listenerCollection.Update();
				worldTime.Total += worldTime.Delta;
			}
		}

		class TestCreateSystem : AppSystem
		{
			private GameWorld            gameWorld;
			private SerializerCollection serializerCollection;
			
			public TestCreateSystem(WorldCollection collection) : base(collection)
			{
				DependencyResolver.Add(() => ref gameWorld);
				DependencyResolver.Add(() => ref serializerCollection);
			}

			public struct IndexComponent : IComponentData
			{
				public int Value;

				public struct Snapshot : IReadWriteSnapshotData<Snapshot>, ISnapshotSyncWithComponent<IndexComponent>
				{
					public uint Tick { get; set; }

					public int Value;

					public void Serialize(in BitBuffer buffer, in Snapshot baseline)
					{
						buffer.AddIntDelta(Value, baseline.Value);
					}

					public void Deserialize(in BitBuffer buffer, in Snapshot baseline)
					{
						Value = buffer.ReadIntDelta(baseline.Value);
					}

					public void FromComponent(in IndexComponent component)
					{
						Value = component.Value;
					}

					public readonly void ToComponent(ref IndexComponent component)
					{
						component.Value = Value;
					}
				}

				public class Serializer : DeltaComponentSerializerBase<Snapshot, IndexComponent>
				{
					public Serializer(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
					{
					}
				}
			}

			private readonly PooledList<GameEntity> origin = new();
			private readonly PooledList<GameEntity> toSerialize = new();
			private readonly PooledList<GameEntity> toOwn = new();
			protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
			{
				base.OnDependenciesResolved(dependencies);
				
				serializerCollection.Register(instigator => new IndexComponent.Serializer(instigator, Context));

				var ent = gameWorld.CreateEntity();
				var own = gameWorld.CreateEntity();
				origin.Add(gameWorld.Safe(ent));
				origin.Add(gameWorld.Safe(own));
				toSerialize.Add(gameWorld.Safe(ent));
				toSerialize.Add(gameWorld.Safe(own));
				toOwn.Add(gameWorld.Safe(own));

				gameWorld.AddComponent(ent, new IndexComponent {Value = 1});
			}

			protected override void OnUpdate()
			{
				base.OnUpdate();

				if (World.Mgr.Get<BroadcastInstigator>().IsEmpty)
					return;

				var broadcaster = World.Mgr.Get<BroadcastInstigator>()[0];
				
				Console.WriteLine("yes");
				foreach (var ent in gameWorld.Boards.Entity.Alive)
				{
					if (!gameWorld.HasComponent<SnapshotEntity>(ent))
						continue;

					var snapshotEntity = gameWorld.GetComponentData<SnapshotEntity>(ent);
					if (snapshotEntity.Instigator == broadcaster.InstigatorId && !origin.Contains(gameWorld.Safe(ent)))
					{
						Console.WriteLine($"  {ent} is the remote version of {snapshotEntity.Source}");
					}

					Console.WriteLine($"  {gameWorld.Safe(ent)}, {snapshotEntity.Source}; {snapshotEntity.Instigator}, {broadcaster.InstigatorId}");
					/*if (snapshotEntity.Instigator != broadcaster.InstigatorId)
					{
						if (!toSerialize.Contains(gameWorld.Safe(ent)))
							toSerialize.Add(gameWorld.Safe(ent));
					}*/

					if (gameWorld.HasComponent<IndexComponent.Snapshot>(ent))
					{
						var buffer = gameWorld.GetBuffer<IndexComponent.Snapshot>(ent);
						var last   = buffer.LastOrDefault();
						Console.WriteLine($"Tick={last.Tick}, Value={last.Value}");
					}
				}

				foreach (var ent in toSerialize)
				{
					Console.WriteLine("  Serialize: " + ent);
					broadcaster.QueuedEntities[ent] = EntitySnapshotPriority.NoPriority;
				}

				foreach (var client in broadcaster.clients)
				{
					foreach (var ent in toOwn)
						client.OwnedEntities.Add(new ClientOwnedEntity(ent, 0, default));
				}
			}
		}
	}
}
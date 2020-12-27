using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	/* Generally, servers broadcaster should have their GroupId at 0.
	 * Clients broadcaster should have the same GroupId as the one that is assigned when receiving data.
	 * But when you add the server as a client in a client broadcaster, you should put it with the GroupId at 0.
	 */
	public partial class BroadcastInstigator : AppObject, ISnapshotInstigator
	{
		public readonly PooledList<ClientSnapshotInstigator> clients;

		private readonly Serialization                         serialization = new();
		private readonly PooledList<ISnapshotSerializerSystem> snapshotSerializers;
		private          GameWorld                             gameWorld;

		/// <summary>
		///     Constructor
		/// </summary>
		/// <param name="storage">
		///     The entity storage for this instigator (if you use a custom one, you will need to manually
		///     dispose it)
		/// </param>
		/// <param name="groupId">The ID of this instigator. If its a server, it should be 0.</param>
		/// <param name="context">Data Context</param>
		public BroadcastInstigator(Entity? storage, int groupId, Context context) : base(context)
		{
			AddDisposable(clients     = new PooledList<ClientSnapshotInstigator>());
			AddDisposable(snapshotSerializers = new PooledList<ISnapshotSerializerSystem>());
			AddDisposable(Serializers = new PooledDictionary<uint, IInstigatorSystem>());

			State = new SnapshotState(groupId);

			if (storage != null)
			{
				Storage = storage.Value;
			}
			else
			{
				Storage = new ContextBindingStrategy(context, true).Resolve<World>()
				                                                   .CreateEntity();
				AddDisposable(Storage);
			}
			
			Storage.Set<ISnapshotInstigator>(this);

			QueuedEntities = new EntityGroup();
			InstigatorId   = groupId;

			var queuedEntity = Storage.World.CreateEntity();
			queuedEntity.SetAsChildOf(Storage);

			DependencyResolver.Add(() => ref gameWorld);
			DependencyResolver.TryComplete();
		}

		public EntityGroup QueuedEntities { get; }

		public PooledDictionary<uint, IInstigatorSystem> Serializers { get; }


		public Entity         Storage { get; }
		public ISnapshotState State   { get; }

		public int InstigatorId { get; }

		public bool TryGetClient(int clientId, [NotNullWhen(true)] out ClientSnapshotInstigator client)
		{
			foreach (var c in clients)
			{
				if (c.InstigatorId == clientId)
				{
					client = c;
					return true;
				}
			}

			client = null!;
			return false;
		}

		public ClientSnapshotInstigator AddClient(int clientId)
		{
			if (clientId == InstigatorId)
				throw new InvalidOperationException("clientId shouldn't be GroupId");

			var client = new ClientSnapshotInstigator(Storage.World.CreateEntity(), clientId, this, gameWorld)
			{
				Serializers = Serializers
			};

			client.GetClientState(out _).Operation = ClientState.EOperation.RecreateFull;

			clients.Add(client);
			return client;
		}

		public void RemoveClient(int clientId)
		{
			foreach (var instigator in clients.Where(c => c.InstigatorId == clientId))
			{
				// Make sure that this client also get removed from MergeGroups
				foreach (var (_, collection) in serialization.groupsPerSystem)
				{
					collection.SetToGroup(instigator.Storage, null);
				}

				foreach (var (id, obj) in instigator.Serializers)
				{
					if (obj is ISnapshotSerializerSystem serializer)
						serializer.OnReset(instigator);
				}

				instigator.Serializers = null;
				instigator.Storage.Dispose();
				instigator.OwnedEntities.Dispose();
			}

			var count = clients.RemoveAll(c => c.InstigatorId == clientId);
			if (count == 0)
				return;

			Console.WriteLine("Removed client #" + clientId);
		}

		private uint serializerId = 1;
		public void SetSerializer(uint id, IInstigatorSystem instigatorSystem, bool disposeOnThisDispose = true)
		{
			if (instigatorSystem is ISnapshotSerializerSystem { } serializer)
				snapshotSerializers.Add(serializer);

			instigatorSystem.System                 = new InstigatorSystem(id);
			Serializers[instigatorSystem.System.Id] = instigatorSystem;

			if (instigatorSystem is AppObject appObject)
				appObject.DependencyResolver.TryComplete();
		}

		public void AddSerializer(IInstigatorSystem serializer, bool disposeOnThisDispose = true)
		{
			SetSerializer(serializerId++, serializer, disposeOnThisDispose);
		}

		public void Serialize(uint tick)
		{
#if DEBUG
			foreach (var serializer in snapshotSerializers)
			{
				if (serializer is AppObject appObject && appObject.DependencyResolver.Dependencies.Count > 0)
				{
					Console.WriteLine($"{serializer.Identifier} still has dependencies!");
				}
			}
#endif
			serialization.Execute(tick, this);
		}
	}
}
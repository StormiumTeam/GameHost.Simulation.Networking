using System;
using System.Buffers;
using System.Threading;
using GameHost.Applications;
using GameHost.Core;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.Application;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.Time;
using GameHost.Worlds.Components;
using RevolutionSnapshot.Core.Buffers;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	[RestrictToApplication(typeof(SimulationApplication))]
	[UpdateAfter(typeof(UpdateDriverSystem))]
	public class SendSnapshotSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		private GameWorld          gameWorld;
		private UpdateDriverSystem driverSystem;

		public SendSnapshotSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref gameWorld);
			DependencyResolver.Add(() => ref driverSystem);
		}

		private GameTime gameTime;
		
		public bool     UseCustomGameTime { get; set; }

		public GameTime GameTime
		{
			get => gameTime;
			set => gameTime = value;
		}

		public override bool CanUpdate()
		{
			return base.CanUpdate() && !UseCustomGameTime && gameWorld.TryGetSingleton(out gameTime) || UseCustomGameTime;
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();
			
			foreach (var (entity, feature) in Features)
			{
				if (!entity.TryGet(out BroadcastInstigator instigator))
					continue;

				Serialize(feature, instigator);
			}
		}

		private void Serialize(MultiplayerFeature feature, BroadcastInstigator instigator)
		{
			if (instigator.DependencyResolver.Dependencies.Count != 0)
				return;
			
			instigator.Serialize((uint) gameTime.Frame);

			Span<TransportConnection> connections = stackalloc TransportConnection[feature.Driver.GetConnectionCount()];
			{
				feature.Driver.GetConnections(connections);
			}
			
			foreach (var connection in connections)
			{
				ClientSnapshotInstigator? client = null;
				if (instigator.InstigatorId == 0 && !instigator.TryGetClient(driverSystem.conClientIdMap.Forward[connection.Id], out client))
					continue;
				if (instigator.InstigatorId > 0 && !instigator.TryGetClient(0, out client))
					continue;
				if (client == null)
					throw new InvalidOperationException("what?");

				var clientData  = client.GetClientData();
				var pooledArray = ArrayPool<byte>.Shared.Rent(clientData.Length);
				{
					var buffer = new DataBufferWriter(0);
					using (buffer)
					{
						buffer.WriteValue(NetCodeMessageType.Snapshot);
						buffer.WriteValue(gameTime);
						buffer.WriteInt(clientData.Length);

						clientData.readPosition = 0;

						var length = clientData.Length;
						clientData.ToSpan(pooledArray);
						
						buffer.WriteSpan(pooledArray.AsSpan(0, length));
						
						feature.Driver.Send(feature.ReliableChannel, connection, buffer.Span);
					}
				}
				ArrayPool<byte>.Shared.Return(pooledArray);
			}
		}
	}
}
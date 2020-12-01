using System;
using System.Buffers;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Worlds.Components;
using RevolutionSnapshot.Core.Buffers;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public class SendSnapshotSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		private GameWorld         gameWorld;
		private IManagedWorldTime worldTime;

		public SendSnapshotSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref gameWorld);
			DependencyResolver.Add(() => ref worldTime);
		}

		public uint? Tick { get; set; }

		protected override void OnUpdate()
		{
			base.OnUpdate();
			
			var tickFormat = Tick ?? (uint) worldTime.Total.Ticks;
			foreach (var (entity, feature) in Features)
			{
				if (!entity.TryGet(out BroadcastInstigator instigator))
					continue;

				Serialize(tickFormat, feature, instigator);
			}
		}

		private void Serialize(uint tickFormat, MultiplayerFeature feature, BroadcastInstigator instigator)
		{
			if (instigator.DependencyResolver.Dependencies.Count != 0)
				return;
			
			instigator.Serialize(tickFormat);

			Span<TransportConnection> connections = stackalloc TransportConnection[feature.Driver.GetConnectionCount()];
			{
				feature.Driver.GetConnections(connections);
			}

			Console.WriteLine($"Send from {instigator.InstigatorId}");

			foreach (var connection in connections)
			{
				ClientSnapshotInstigator? client = null;
				if (instigator.InstigatorId == 0 && !instigator.TryGetClient((int) connection.Id, out client))
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
						buffer.WriteValue(TimeSpan.FromTicks(tickFormat));
						buffer.WriteInt(clientData.Length);

						clientData.readPosition = 0;
						clientData.ToSpan(pooledArray);
						
						buffer.WriteSpan(pooledArray.AsSpan(0, clientData.Length));

						feature.Driver.Send(feature.ReliableChannel, connection, buffer.Span);
					}
				}
				ArrayPool<byte>.Shared.Return(pooledArray);
			}
		}
	}
}
using System;
using System.Buffers;
using System.Threading;
using Collections.Pooled;
using GameHost.Applications;
using GameHost.Core;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.IO;
using GameHost.Revolution.NetCode.Rpc;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.Application;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.Time;
using GameHost.Worlds.Components;
using K4os.Compression.LZ4;
using RevolutionSnapshot.Core.Buffers;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	[RestrictToApplication(typeof(SimulationApplication))]
	[UpdateAfter(typeof(UpdateDriverSystem))]
	public class SendSnapshotSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		public struct PreSendEvent
		{
			public MultiplayerFeature  Feature;
			public BroadcastInstigator Instigator;
		}
		
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

				if (entity.TryGet(out NetCodeRpcBroadcaster rpcBroadcaster))
					SendRpc(feature, rpcBroadcaster);
				SendSnapshot(feature, instigator);
			}
		}

		private PooledList<byte> bytes = new();

		private void SendRpc(MultiplayerFeature feature, NetCodeRpcBroadcaster broadcaster)
		{
			if (broadcaster.DependencyResolver.Dependencies.Count != 0)
				return;

			bytes.Clear();
			var dataLength = broadcaster.GetData(bytes);
			if (bytes.Count == 0 || dataLength == 0)
				return;

			var buffer = new DataBufferWriter(dataLength);
			using (buffer)
			{
				buffer.WriteValue(NetCodeMessageType.Rpc);
				buffer.WriteCompressed(bytes.Span.Slice(0, dataLength));

				feature.Driver.Broadcast(feature.ReliableChannel, buffer.Span);
			}

			broadcaster.Clear();
		}

		private PooledList<TransportConnection> tempConnectionList = new();
		private void SendSnapshot(MultiplayerFeature feature, BroadcastInstigator instigator)
		{
			if (instigator.DependencyResolver.Dependencies.Count != 0)
				return;

			World.Mgr.Publish(new PreSendEvent {Feature = feature, Instigator = instigator});
			
			instigator.Serialize((uint) gameTime.Frame);

			tempConnectionList.Clear();
			Span<TransportConnection> connections = tempConnectionList.AddSpan(feature.Driver.GetConnectionCount());
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

				var clientData = client.GetClientData();
				using (DisposableArray.Rent(clientData.Length, out var pooledArray))
				{
					var buffer = new DataBufferWriter(0);
					using (buffer)
					{
						buffer.WriteValue(NetCodeMessageType.Snapshot);
						buffer.WriteValue(gameTime);

						clientData.readPosition = 0;

						var length = clientData.Length;
						clientData.ToSpan(pooledArray);
						
						var size = buffer.WriteCompressed(pooledArray.AsSpan(0, length), LZ4Level.L12_MAX);
						if (instigator.InstigatorId == 0)
							Console.WriteLine($"Size={size}");

						feature.Driver.Send(feature.ReliableChannel, connection, buffer.Span);
					}
				}
			}
		}
	}
}
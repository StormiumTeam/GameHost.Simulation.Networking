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

				Serialize(feature, instigator);
			}
		}
		
		// tbh, I don't know I could force systems to be serializer RIGHT before this system (SendSnapshotSystem) is called
		public Bindable<(MultiplayerFeature feature, BroadcastInstigator instigator)> beforeNewFeatureSerialization = new(); 

		private void Serialize(MultiplayerFeature feature, BroadcastInstigator instigator)
		{
			if (instigator.DependencyResolver.Dependencies.Count != 0)
				return;

			World.Mgr.Publish(new PreSendEvent {Feature = feature, Instigator = instigator});
			
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

				var clientData = client.GetClientData();

				var compressedSize = LZ4Codec.MaximumOutputSize(clientData.Length);
				var pooledArray    = ArrayPool<byte>.Shared.Rent(clientData.Length);
				{
					var buffer = new DataBufferWriter(0);
					using (buffer)
					{
						buffer.WriteValue(NetCodeMessageType.Snapshot);
						buffer.WriteValue(gameTime);

						var compressedMarker = buffer.WriteInt(compressedSize);
						buffer.WriteInt(clientData.Length);

						clientData.readPosition = 0;

						var length = clientData.Length;
						clientData.ToSpan(pooledArray);

						buffer.Capacity += compressedSize;

						const LZ4Level encoder = LZ4Level.L04_HC;

						var size = LZ4Codec.Encode(pooledArray.AsSpan(0, length), buffer.CapacitySpan.Slice(buffer.Length, compressedSize), encoder);
						buffer.WriteInt(size, compressedMarker);

						buffer.Length += compressedSize;

						feature.Driver.Send(feature.ReliableChannel, connection, buffer.Span);
					}
				}
				ArrayPool<byte>.Shared.Return(pooledArray);
			}
		}
	}
}
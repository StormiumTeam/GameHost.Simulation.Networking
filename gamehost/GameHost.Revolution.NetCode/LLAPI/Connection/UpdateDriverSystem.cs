using System;
using DefaultEcs;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using Microsoft.Extensions.Logging;
using RevolutionSnapshot.Core.Buffers;
using ZLogger;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public class UpdateDriverSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		private SerializerCollection serializerCollection;
		private SendSystems          sendSystems;
		private ILogger              logger;

		public UpdateDriverSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref serializerCollection);
			DependencyResolver.Add(() => ref sendSystems);
			DependencyResolver.Add(() => ref logger);
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			foreach (var (entity, feature) in Features)
			{
				while (feature.Driver.Accept().IsCreated)
				{
				}

				feature.Driver.Update();

				TransportEvent ev;
				while ((ev = feature.Driver.PopEvent()).Type != TransportEvent.EType.None)
				{
					Console.WriteLine($"{ev.Type}");
					switch (ev.Type)
					{
						case TransportEvent.EType.Connect:
							if (feature is ServerFeature serverFeature)
							{
								using var writer = new DataBufferWriter(8);
								writer.WriteValue(NetCodeMessageType.ClientConnection);
								writer.WriteInt((int) ev.Connection.Id);

								var client = entity.Get<BroadcastInstigator>().AddClient((int) ev.Connection.Id);
								client.Storage.Set(ev.Connection);

								feature.Driver.Send(feature.ReliableChannel, ev.Connection, writer.Span);

								// Send snapshot systems to this client
								sendSystems.SendToClient(entity, serverFeature, ev.Connection);
							}

							break;

						case TransportEvent.EType.Data:
							var reader = new DataBufferReader(ev.Data);
							ReadNetCodeMessage((entity, feature), ev, reader);
							break;
					}
				}
			}
		}

		private void ReadNetCodeMessage((Entity, MultiplayerFeature) featureArgs, TransportEvent ev, DataBufferReader reader)
		{
			var (entity, feature) = featureArgs;
			var messageType = reader.ReadValue<NetCodeMessageType>();
			switch (messageType)
			{
				case NetCodeMessageType.ClientConnection:
				{
					if (feature is not ClientFeature)
						throw new InvalidOperationException("Why did we received a client message on a server feature?");

					var instigatorId = reader.ReadValue<int>();
					var broadcaster  = new BroadcastInstigator(entity, instigatorId, Context);
					var serverClient = broadcaster.AddClient(0);
					entity.Set(broadcaster);
					entity.Set(serverClient);
					Console.WriteLine($"Assigning Client data... InstigatorId={instigatorId}");

					break;
				}
				case NetCodeMessageType.Snapshot:
				{
					var currentTick  = reader.ReadValue<TimeSpan>();
					var snapshotSpan = reader.ReadSpanDirect<byte>();

					if (!entity.TryGet(out BroadcastInstigator broadcaster))
					{
						throw new InvalidOperationException("where banana");
					}

					ClientSnapshotInstigator client;
					if (feature is ClientFeature)
					{
						if (!broadcaster.TryGetClient(0, out client))
							throw new InvalidOperationException("wtf banana");
					}
					else
					{
						if (!broadcaster.TryGetClient((int) ev.Connection.Id, out client))
							throw new InvalidOperationException($"No client found with Id={ev.Connection.Id}");
					}

					if (client == null)
						throw new InvalidOperationException("??");

					client.Deserialize(snapshotSpan);
					break;
				}
				case NetCodeMessageType.SendSnapshotSystems:
				{
					// TODO: Disconnect the client
					if (feature is ServerFeature)
						throw new InvalidOperationException("It's not allowed to receive snapshot systems on a server");

					// At this point we should have been assigned with a broadcaster...
					if (!entity.TryGet(out BroadcastInstigator broadcaster))
					{
						throw new InvalidOperationException("where banana");
					}

					Console.WriteLine("Received server systems!");
					var serializers = broadcaster.Serializers;

					var count          = reader.ReadValue<int>();
					var hadReplacement = false;
					while (count-- > 0)
					{
						var systemId   = reader.ReadValue<uint>();
						var systemName = reader.ReadString();

						var replace = true;
						if (serializers.TryGetValue(systemId, out var existing))
						{
							if (existing.Identifier == systemName)
								replace = false;
							else if (existing is IDisposable disposable)
								disposable.Dispose();
						}

						if (replace)
						{
							if (!serializerCollection.TryGet(systemName, out var output))
							{
								logger.ZLogError("No Serializer register with name {0}", systemName);
								continue;
							}

							broadcaster.SetSerializer(systemId, output.create(broadcaster));
							Console.WriteLine($"Add {systemName} as {systemId}");

							hadReplacement = true;
						}
					}

					// If a system has been added/replaced, we need to remove prior archetypes from entities
					if (hadReplacement)
					{
						(broadcaster.State as BroadcastInstigator.SnapshotState).ClearAllAssignedArchetype();
					}

					break;
				}
				default:
					throw new ArgumentOutOfRangeException("netcodemsg_type");
			}
		}
	}
}
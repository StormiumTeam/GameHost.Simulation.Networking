using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using BidirectionalMap;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.Application;
using GameHost.Simulation.Utility.Time;
using K4os.Compression.LZ4;
using Microsoft.Extensions.Logging;
using RevolutionSnapshot.Core.Buffers;
using ZLogger;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	[RestrictToApplication(typeof(SimulationApplication))]
	public class UpdateDriverSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		private SerializerCollection serializerCollection;
		private SendSystems          sendSystems;
		private ILogger              logger;

		private  int              serverCountNextId = 1;
		internal BiMap<uint, int> conClientIdMap    = new();

		private PooledList<byte> bytesList = new();

		public UpdateDriverSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref serializerCollection);
			DependencyResolver.Add(() => ref sendSystems);
			DependencyResolver.Add(() => ref logger);
			
			AddDisposable(bytesList);
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			foreach (var (entity, feature) in Features)
			{
				if (entity.TryGet(out BroadcastInstigator broadcaster))
				{
					foreach (var client in broadcaster.clients)
					{
						if (!client.Storage.TryGet(out List<GameTime> times))
							continue;
						
						times.Clear();
					}
				}
				
				feature.Driver.Update();
				
				while (feature.Driver.Accept().IsCreated)
				{
				}
				
				TransportEvent ev;
				while ((ev = feature.Driver.PopEvent()).Type != TransportEvent.EType.None)
				{
					switch (ev.Type)
					{
						case TransportEvent.EType.Connect:
						{
							if (feature is ServerFeature serverFeature)
							{
								using var writer = new DataBufferWriter(8);
								writer.WriteValue(NetCodeMessageType.ClientConnection);
								writer.WriteInt(serverCountNextId);

								var client = entity.Get<BroadcastInstigator>().AddClient(serverCountNextId);
								client.Storage.Set(ev.Connection);

								feature.Driver.Send(feature.ReliableChannel, ev.Connection, writer.Span);

								conClientIdMap.Add(ev.Connection.Id, serverCountNextId);
								Console.WriteLine($"Register Client {ev.Connection} --> {serverCountNextId}");
								serverCountNextId++;

								// Send snapshot systems to this client
								sendSystems.SendToClient(entity, serverFeature, ev.Connection);
							}

							break;
						}

						case TransportEvent.EType.Data:
							var reader = new DataBufferReader(ev.Data);
							ReadNetCodeMessage((entity, feature), ev, reader);
							break;

						case TransportEvent.EType.Disconnect:
						{
							if (feature is ServerFeature serverFeature
							    && entity.TryGet(out BroadcastInstigator broadcast))
							{
								if (!broadcast.TryGetClient(conClientIdMap.Forward[ev.Connection.Id], out var client))
								{
									logger.ZLogWarning("A client (id={0}) has been disconnected but had no {1}.", ev.Connection.Id, nameof(ClientSnapshotInstigator));
									continue;
								}

								broadcast.RemoveClient(client.InstigatorId);
							}

							break;
						}
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
					var gameTime     = reader.ReadValue<GameTime>();

					var compressedSize   = reader.ReadValue<int>();
					var uncompressedSize = reader.ReadValue<int>();

					bytesList.Clear();
					var uncompressed = bytesList.AddSpan(uncompressedSize);
					
					unsafe
					{
						var compressed = new Span<byte>(reader.DataPtr + reader.GetReadIndexAndSetNew(default, compressedSize * sizeof(byte)), compressedSize);
						LZ4Codec.Decode(compressed, uncompressed);
					}

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
						if (!broadcaster.TryGetClient(conClientIdMap.Forward[ev.Connection.Id], out client))
							throw new InvalidOperationException($"No client found with Id={ev.Connection.Id}");
					}

					if (client == null)
						throw new InvalidOperationException("??");

					client.Deserialize(uncompressed);
					if (!client.Storage.TryGet(out List<GameTime> times))
						client.Storage.Set(times = new List<GameTime>());

					if (times.Count > Consts.TIME_HISTORY)
						times.RemoveAt(0);
					times.Add(gameTime);
					client.Storage.Set(gameTime);
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
							//Console.WriteLine($"Add {systemName} as {systemId}");

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
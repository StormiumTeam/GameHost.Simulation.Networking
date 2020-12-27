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
using GameHost.Revolution.NetCode.Rpc;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.Application;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.Time;
using K4os.Compression.LZ4;
using Microsoft.Extensions.Logging;
using RevolutionSnapshot.Core.Buffers;
using ZLogger;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public struct OnSnapshotReceivedMessage
	{
		public Entity FeatureEntity;
	}

	[RestrictToApplication(typeof(SimulationApplication))]
	public class UpdateDriverSystem : AppSystemWithFeature<MultiplayerFeature>
	{
		private GameWorld            gameWorld;
		private SerializerCollection serializerCollection;
		private SendSystems          sendSystems;
		private ILogger              logger;

		private  int              serverCountNextId = 1;
		internal BiMap<uint, int> conClientIdMap    = new();

		private PooledList<byte> bytesList = new();

		public UpdateDriverSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref gameWorld);
			DependencyResolver.Add(() => ref serializerCollection);
			DependencyResolver.Add(() => ref sendSystems);
			DependencyResolver.Add(() => ref logger);
			
			AddDisposable(bytesList);
		}

		private EntityQuery snapshotQuery;
		public override bool CanUpdate()
		{
			if (Features.Count == 0)
			{
				// TODO: use own created BiMap
				if (conClientIdMap.Count() > 0)
					conClientIdMap = new BiMap<uint, int>();

				if (snapshotQuery == null)
					snapshotQuery = new EntityQuery(gameWorld, new[]
					{
						gameWorld.AsComponentType<SnapshotEntity>()
					}, new []
					{
						gameWorld.AsComponentType<SnapshotEntity.CreatedByThisWorld>()
					});
				
				snapshotQuery.RemoveAllEntities();
			}
			
			return base.CanUpdate();
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
								client.Storage.Set(new NetCodeRpcBroadcaster(Context, client));

								feature.Driver.Send(feature.ReliableChannel, ev.Connection, writer.Span);

								conClientIdMap.Remove(ev.Connection.Id);
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
							ReadNetCodeMessage((entity, feature), ev, ref reader);
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

		private void ReadNetCodeMessage((Entity, MultiplayerFeature) featureArgs, TransportEvent ev, ref DataBufferReader reader)
		{
			var (entity, feature) = featureArgs;
			var messageType = reader.ReadValue<NetCodeMessageType>();
			if (reader.CurrReadIndex != 1)
				throw new InvalidOperationException("invalid readIndex (perhaps weird processor?)");

			/*if (featureArgs.Item2 is ClientFeature)
				Console.WriteLine($"client read {reader.Length}B for {messageType}");*/
			
			switch (messageType)
			{
				case NetCodeMessageType.ClientConnection:
				{
					if (feature is not ClientFeature)
						throw new InvalidOperationException("Why did we received a client message on a server feature?");

					var instigatorId = reader.ReadValue<int>();
					var broadcaster  = new BroadcastInstigator(entity, instigatorId, Context);
					var rpc          = new NetCodeRpcBroadcaster(Context, broadcaster);
					var serverClient = broadcaster.AddClient(0);
					entity.Set(broadcaster);
					entity.Set(rpc);
					entity.Set(serverClient);
					Console.WriteLine($"Assigning Client data... InstigatorId={instigatorId}");

					break;
				}
				case NetCodeMessageType.Rpc:
				{
					bytesList.Clear();
					var uncompressed = reader.ReadDecompressed(bytesList);

					if (!entity.TryGet(out BroadcastInstigator broadcaster))
					{
						throw new InvalidOperationException("where banana7");
					}

					ClientSnapshotInstigator client;
					if (feature is ClientFeature)
					{
						if (!broadcaster.TryGetClient(0, out client))
							throw new InvalidOperationException("wtf banana8");
					}
					else
					{
						if (!broadcaster.TryGetClient(conClientIdMap.Forward[ev.Connection.Id], out client))
							throw new InvalidOperationException($"No client found with Id={ev.Connection.Id}");
					}

					if (client == null)
						throw new InvalidOperationException("??");

					if (!client.Storage.TryGet(out NetCodeRpcBroadcaster rpcBroadcaster))
						throw new InvalidOperationException("No RpcBroadcaster found on client");

					rpcBroadcaster.Receive(uncompressed);
					break;
				}
				case NetCodeMessageType.Snapshot:
				{
					var gameTime     = reader.ReadValue<GameTime>();

					bytesList.Clear();
					var uncompressed = reader.ReadDecompressed(bytesList);

					if (!entity.TryGet(out BroadcastInstigator broadcaster))
					{
						throw new InvalidOperationException("where banana7");
					}

					ClientSnapshotInstigator client;
					if (feature is ClientFeature)
					{
						if (!broadcaster.TryGetClient(0, out client))
							throw new InvalidOperationException("wtf banana8");
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
					
					World.Mgr.Publish(new OnSnapshotReceivedMessage {FeatureEntity = entity});
					
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
						throw new InvalidOperationException("where banana9");
					}

					Console.WriteLine("Received server systems!");
					var serializers = broadcaster.Serializers;
					
					bytesList.Clear();
					var uncompressed = reader.ReadDecompressed(bytesList);
					reader = new DataBufferReader(uncompressed);

					var count          = reader.ReadValue<int>();
					var hadReplacement = false;

					var print = "";
					while (count-- > 0)
					{
						var systemId   = reader.ReadValue<uint>();
						var systemName = reader.ReadString();
						
						print += $"{systemId} - {systemName}\n";

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

					Console.WriteLine(print);

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
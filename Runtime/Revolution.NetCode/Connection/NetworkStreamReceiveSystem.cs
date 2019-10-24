using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Revolution.NetCode
{
    [UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
    [AlwaysUpdateSystem]
    public class NetworkStreamReceiveSystem : JobComponentSystem
    {
        // --
        // Public fields

        public   UdpNetworkDriver            Driver           => m_Driver;
        internal UdpNetworkDriver.Concurrent ConcurrentDriver => m_ConcurrentDriver;

        public NetworkPipeline UnreliablePipeline => m_UnreliablePipeline;
        public NetworkPipeline RpcPipeline        => m_RpcPipeline;
        public NetworkPipeline SnapshotPipeline   => m_SnapshotPipeline;

        // --
        // Private fields

        // --------- --------- --------- --------- //
        // Drivers
        // --------- --------- --------- --------- //
        private UdpNetworkDriver            m_Driver;
        private UdpNetworkDriver.Concurrent m_ConcurrentDriver;

        // --------- --------- --------- --------- //
        // Pipelines
        // --------- --------- --------- --------- //
        private NetworkPipeline m_UnreliablePipeline;

        // Rpc and Snapshot stream possess the same pipeline type.
        private NetworkPipeline m_RpcPipeline;
        private NetworkPipeline m_SnapshotPipeline;

        private bool                                     m_DriverListening;
        private NativeArray<int>                         numNetworkIds;
        private NativeQueue<int>                         freeNetworkIds;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;
        private DefaultRpcProcessSystem<RpcSetNetworkId> rpcQueue;
        private int                                      m_ClientPacketDelay;
        private int                                      m_ClientPacketDrop;

        private EntityQuery m_AssignQuery;
        private EntityQuery m_ConnectionReceiveQuery;

        public bool Listen(NetworkEndPoint endpoint)
        {
            if (m_UnreliablePipeline == NetworkPipeline.Null)
                m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(NullPipelineStage));
            if (m_RpcPipeline == NetworkPipeline.Null)
                m_RpcPipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            if (m_SnapshotPipeline == NetworkPipeline.Null)
                m_SnapshotPipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));

            // Switching to server mode
            if (m_Driver.Bind(endpoint) != 0)
                return false;
            if (m_Driver.Listen() != 0)
                return false;
            m_DriverListening = true;
            // FIXME: Bind breaks all copies of the driver nad makes them send to the wrong socket
            m_ConcurrentDriver = m_Driver.ToConcurrent();
            return true;
        }

        public Entity Connect(NetworkEndPoint endpoint)
        {
            if (m_UnreliablePipeline == NetworkPipeline.Null)
            {
                if (m_ClientPacketDrop > 0 || m_ClientPacketDelay > 0)
                    m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(SimulatorPipelineStage), typeof(SimulatorPipelineStageInSend));
                else
                    m_UnreliablePipeline = m_Driver.CreatePipeline(typeof(NullPipelineStage));
            }

            if (m_RpcPipeline == NetworkPipeline.Null)
            {
                if (m_ClientPacketDelay > 0 || m_ClientPacketDrop > 0)
                    m_RpcPipeline = m_Driver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
                else
                    m_RpcPipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            }

            if (m_SnapshotPipeline == NetworkPipeline.Null)
            {
                if (m_ClientPacketDelay > 0 || m_ClientPacketDrop > 0)
                    m_SnapshotPipeline = m_Driver.CreatePipeline(typeof(SimulatorPipelineStageInSend), typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
                else
                    m_SnapshotPipeline = m_Driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            }

            var ent = EntityManager.CreateEntity();
            EntityManager.AddComponentData(ent, new NetworkStreamConnection {Value = m_Driver.Connect(endpoint)});
            EntityManager.AddComponentData(ent, new NetworkSnapshotAckComponent());
            EntityManager.AddComponentData(ent, new CommandTargetComponent());

            EntityManager.AddBuffer<OutgoingRpcDataStreamBufferComponent>(ent).Reserve(100);
            EntityManager.AddBuffer<IncomingCommandDataStreamBufferComponent>(ent).Reserve(100);
            EntityManager.AddBuffer<IncomingSnapshotStreamBufferComponent>(ent).Reserve(100);
            EntityManager.AddBuffer<IncomingRpcDataStreamBufferComponent>(ent).Reserve(100);
            return ent;
        }

        protected override void OnCreate()
        {
            var reliabilityParams = new ReliableUtility.Parameters {WindowSize = 32};

            if (UnityEngine.Debug.isDebugBuild)
            {
                m_ClientPacketDelay = UnityEngine.PlayerPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_ClientDelay");
                m_ClientPacketDrop  = UnityEngine.PlayerPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_ClientDropRate");
                int networkRate = 60; // TODO: read from some better place
                // All 3 packet types every frame stored for maximum delay, doubled for safety margin
                int maxPackets = 2 * (networkRate * 3 * m_ClientPacketDelay + 999) / 1000;
                var simulatorParams = new SimulatorUtility.Parameters
                    {MaxPacketSize = NetworkParameterConstants.MTU, MaxPacketCount = maxPackets, PacketDelayMs = m_ClientPacketDelay, PacketDropPercentage = m_ClientPacketDrop};
                m_Driver = new UdpNetworkDriver(simulatorParams, reliabilityParams);
                UnityEngine.Debug.Log("Using simulator with latency=" + m_ClientPacketDelay + " packet drop=" + m_ClientPacketDrop);
            }
            else
                m_Driver = new UdpNetworkDriver(reliabilityParams);

            m_ConcurrentDriver   = m_Driver.ToConcurrent();
            m_UnreliablePipeline = NetworkPipeline.Null;
            m_RpcPipeline        = NetworkPipeline.Null;
            m_UnreliablePipeline = NetworkPipeline.Null;
            m_DriverListening    = false;
            m_Barrier            = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            numNetworkIds        = new NativeArray<int>(1, Allocator.Persistent);
            freeNetworkIds       = new NativeQueue<int>(Allocator.Persistent);
            rpcQueue             = World.GetOrCreateSystem<DefaultRpcProcessSystem<RpcSetNetworkId>>();

            m_AssignQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new ComponentType[] {typeof(NetworkStreamConnection)},
                None = new ComponentType[] {typeof(NetworkIdComponent)}
            });
            m_ConnectionReceiveQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkSnapshotAckComponent)},
                None = new ComponentType[] {typeof(NetworkStreamDisconnected)}
            });
        }

        protected override void OnDestroy()
        {
            numNetworkIds.Dispose();
            freeNetworkIds.Dispose();
            m_Driver.Dispose();
        }

        struct ConnectionAcceptJob : IJob
        {
            public EntityCommandBuffer commandBuffer;
            public UdpNetworkDriver    driver;

            public void Execute()
            {
                NetworkConnection con;
                while ((con = driver.Accept()) != default)
                {
                    // New connection can never have any events, if this one does - just close it
                    DataStreamReader reader;
                    if (con.PopEvent(driver, out reader) != NetworkEvent.Type.Empty)
                    {
                        con.Disconnect(driver);
                        continue;
                    }

                    // create an entity for the new connection
                    var ent = commandBuffer.CreateEntity();
                    commandBuffer.AddComponent(ent, new NetworkStreamConnection {Value = con});
                    commandBuffer.AddComponent(ent, new NetworkSnapshotAckComponent());
                    commandBuffer.AddComponent(ent, new CommandTargetComponent());
                    commandBuffer.AddBuffer<OutgoingRpcDataStreamBufferComponent>(ent);
                    commandBuffer.AddBuffer<IncomingCommandDataStreamBufferComponent>(ent);
                    commandBuffer.AddBuffer<IncomingRpcDataStreamBufferComponent>(ent);
                }
            }
        }

        [ExcludeComponent(typeof(NetworkIdComponent))]
        struct AssignNetworkIdJob : IJobForEachWithEntity<NetworkStreamConnection>
        {
            public EntityCommandBuffer                                    commandBuffer;
            public NativeArray<int>                                       numNetworkId;
            public NativeQueue<int>                                       freeNetworkIds;
            public RpcQueue<RpcSetNetworkId>                              rpcQueue;
            public BufferFromEntity<OutgoingRpcDataStreamBufferComponent> rpcBuffer;

            public void Execute(Entity entity, int index, [ReadOnly] ref NetworkStreamConnection connection)
            {
                if (!connection.Value.IsCreated)
                    return;
                // Send RPC - assign network id
                int nid;
                if (!freeNetworkIds.TryDequeue(out nid))
                {
                    // Avoid using 0
                    nid             = numNetworkId[0] + 1;
                    numNetworkId[0] = nid;
                }

                commandBuffer.AddComponent(entity, new NetworkIdComponent {Value = nid});
                rpcQueue.Schedule(rpcBuffer[entity], new RpcSetNetworkId {nid    = nid});
            }
        }

        [ExcludeComponent(typeof(NetworkStreamDisconnected))]
        struct ConnectionReceiveJob : IJobForEachWithEntity<NetworkStreamConnection, NetworkSnapshotAckComponent>
        {
            public            EntityCommandBuffer                                        commandBuffer;
            public            UdpNetworkDriver                                driver;
            public            NativeQueue<int>                                           freeNetworkIds;
            public            BufferFromEntity<IncomingRpcDataStreamBufferComponent>     rpcBuffer;
            public            BufferFromEntity<IncomingCommandDataStreamBufferComponent> cmdBuffer;
            public            BufferFromEntity<IncomingSnapshotStreamBufferComponent>    snapshotBuffer;
            [ReadOnly] public ComponentDataFromEntity<NetworkIdComponent>                networkId;
            public            uint                                                       localTime;

            public unsafe void Execute(Entity entity, int index, ref NetworkStreamConnection connection, ref NetworkSnapshotAckComponent snapshotAck)
            {
                if (!connection.Value.IsCreated)
                    return;

                DataStreamReader  reader;
                NetworkEvent.Type evt;

                while ((evt = driver.PopEventForConnection(connection.Value, out reader)) != NetworkEvent.Type.Empty)
                {
                    switch (evt)
                    {
                        case NetworkEvent.Type.Connect:
                            break;
                        case NetworkEvent.Type.Disconnect:
                        {
                            // Flag the connection as lost, it will be deleted in a separate system, giving user code one frame to detect and respond to lost connection
                            commandBuffer.AddComponent(entity, new NetworkStreamDisconnected());

                            connection.Value = default(NetworkConnection);
                            if (networkId.Exists(entity))
                                freeNetworkIds.Enqueue(networkId[entity].Value);
                            return;
                        }

                        case NetworkEvent.Type.Data:
                            // FIXME: do something with the data
                            var ctx = default(DataStreamReader.Context);
                            switch ((NetworkStreamProtocol) reader.ReadByte(ref ctx))
                            {
                                case NetworkStreamProtocol.Command:
                                {
                                    var buffer = cmdBuffer[entity];

                                    // FIXME: should be handle by a custom command stream system
                                    uint snapshot = reader.ReadUInt(ref ctx);
                                    snapshotAck.UpdateReceivedByRemote(snapshot);
                                    uint remoteTime        = reader.ReadUInt(ref ctx);
                                    uint localTimeMinusRTT = reader.ReadUInt(ref ctx);
                                    snapshotAck.UpdateRemoteTime(remoteTime, localTimeMinusRTT, localTime);

                                    int headerSize = 1 + 4 * 3;
                                    buffer.ResizeUninitialized(reader.Length - headerSize);
                                    UnsafeUtility.MemCpy(buffer.GetUnsafePtr(),
                                        reader.GetUnsafeReadOnlyPtr() + headerSize,
                                        reader.Length - headerSize);

                                    break;
                                }

                                case NetworkStreamProtocol.Snapshot:
                                {
                                    uint remoteTime        = reader.ReadUInt(ref ctx);
                                    uint localTimeMinusRTT = reader.ReadUInt(ref ctx);
                                    snapshotAck.UpdateRemoteTime(remoteTime, localTimeMinusRTT, localTime);
                                    int headerSize = 1 + 4 * 2;

                                    var buffer = snapshotBuffer[entity];
                                    var oldLen = buffer.Length;
                                    buffer.ResizeUninitialized(buffer.Length + reader.Length - headerSize);
                                    UnsafeUtility.MemCpy((byte*) buffer.GetUnsafePtr() + oldLen,
                                        reader.GetUnsafeReadOnlyPtr() + headerSize,
                                        reader.Length - headerSize);
                                    break;
                                }

                                case NetworkStreamProtocol.Rpc:
                                {
                                    var buffer = rpcBuffer[entity];
                                    var oldLen = buffer.Length;
                                    buffer.ResizeUninitialized(oldLen + reader.Length - 1);
                                    UnsafeUtility.MemCpy(((byte*) buffer.GetUnsafePtr()) + oldLen,
                                        reader.GetUnsafeReadOnlyPtr() + 1,
                                        reader.Length - 1);

                                    break;
                                }

                                default:
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                                    throw new InvalidOperationException("Received unknown message type");
#else
                        break;
#endif
                            }

                            break;
                        default:
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            throw new InvalidOperationException("Received unknown network event " + evt);
#else
                    break;
#endif
                    }
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            inputDeps = m_Driver.ScheduleUpdate(inputDeps);

            var cmdBuffer = m_Barrier.CreateCommandBuffer();
            if (m_DriverListening)
            {
                // Schedule accept job
                ConnectionAcceptJob acceptJob;
                acceptJob.driver        = m_Driver;
                acceptJob.commandBuffer = cmdBuffer;
                inputDeps               = acceptJob.Schedule(inputDeps);

                // Schedule job to assign network ids to new connections
                AssignNetworkIdJob assignJob;
                assignJob.commandBuffer  = cmdBuffer;
                assignJob.numNetworkId   = numNetworkIds;
                assignJob.freeNetworkIds = freeNetworkIds;
                assignJob.rpcQueue       = rpcQueue.RpcQueue;
                assignJob.rpcBuffer      = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>();
                inputDeps                = assignJob.ScheduleSingle(m_AssignQuery, inputDeps);
            }
            else
            {
                freeNetworkIds.Clear();
            }

            // Schedule parallel update job
            ConnectionReceiveJob recvJob;
            recvJob.commandBuffer  = cmdBuffer;
            recvJob.driver         = m_Driver;
            recvJob.freeNetworkIds = freeNetworkIds;
            recvJob.networkId      = GetComponentDataFromEntity<NetworkIdComponent>();
            recvJob.rpcBuffer      = GetBufferFromEntity<IncomingRpcDataStreamBufferComponent>();
            recvJob.cmdBuffer      = GetBufferFromEntity<IncomingCommandDataStreamBufferComponent>();
            recvJob.snapshotBuffer = GetBufferFromEntity<IncomingSnapshotStreamBufferComponent>();
            recvJob.localTime      = NetworkTimeSystem.TimestampMS;

            inputDeps = recvJob.ScheduleSingle(m_ConnectionReceiveQuery, inputDeps);
            m_Barrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }
    }
}
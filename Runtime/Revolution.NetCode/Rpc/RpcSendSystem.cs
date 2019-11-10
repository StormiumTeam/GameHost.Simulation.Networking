using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
    [UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
    public unsafe class RpcSendSystem : ComponentSystem
    {
        private EntityQuery                m_IncomingDataQuery;
        private RpcCollectionSystem        m_RpcCollectionSystem;
        private NetworkTimeSystem          m_TimeSystem;
        private NetworkStreamReceiveSystemGroup m_ReceiveSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IncomingDataQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(NetworkStreamConnection), typeof(OutgoingRpcDataStreamBufferComponent)}
            });
            m_RpcCollectionSystem = World.GetOrCreateSystem<RpcCollectionSystem>();
            m_ReceiveSystem       = World.GetOrCreateSystem<NetworkStreamReceiveSystemGroup>();
            m_TimeSystem          = World.GetOrCreateSystem<NetworkTimeSystem>();
        }

        protected override void OnUpdate()
        {
            foreach (var system in m_RpcCollectionSystem.SystemProcessors.Values)
                system.Prepare();

            var connectionEntities = m_IncomingDataQuery.ToEntityArray(Allocator.TempJob);
            foreach (var system in m_RpcCollectionSystem.SystemProcessors.Values)
            {
                system.ProcessSend(connectionEntities);
            }
            connectionEntities.Dispose();

            Entities.With(m_IncomingDataQuery).ForEach((ref NetworkStreamConnection connection, DynamicBuffer<OutgoingRpcDataStreamBufferComponent> outgoingData) =>
            {
                if (outgoingData.Length == 0)
                    return;

                var tmp = new DataStreamWriter(outgoingData.Length + sizeof(byte), Allocator.Temp);
                tmp.Write((byte) NetworkStreamProtocol.Rpc);
                tmp.WriteBytes((byte*) outgoingData.GetUnsafePtr(), outgoingData.Length);
                m_ReceiveSystem.QueueData(PipelineType.Rpc, connection.Value, tmp);
                outgoingData.Clear();
            });
        }
    }
}
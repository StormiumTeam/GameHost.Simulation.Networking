using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Unity.NetCode
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    // dependency just for acking
    [UpdateAfter(typeof(NetworkReceiveSnapshotSystemGroup))]
    public sealed class CommandSendSystem : ComponentSystem
    {
        private EntityQuery                m_IncomingDataQuery;
        private CommandCollectionSystem    m_CommandCollectionSystem;
        private ClientSimulationSystemGroup m_ClientSimulationSystemGroup;
        private NetworkStreamReceiveSystem m_ReceiveSystem;
        private NetworkCompressionModel    m_NetworkCompressionModel;

        private uint m_LastServerTick;
        
        protected override void OnCreate()
        {
            base.OnCreate();

            m_IncomingDataQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(CommandTargetComponent), typeof(NetworkStreamInGame)}
            });
            m_CommandCollectionSystem = World.GetOrCreateSystem<CommandCollectionSystem>();
            m_ReceiveSystem           = World.GetOrCreateSystem<NetworkStreamReceiveSystem>();
            m_ClientSimulationSystemGroup = World.GetOrCreateSystem<ClientSimulationSystemGroup>();

            m_NetworkCompressionModel = new NetworkCompressionModel(Allocator.Persistent);
        }

        protected override void OnUpdate()
        {
            foreach (var system in m_CommandCollectionSystem.SystemProcessors.Values)
                system.Prepare();

            var targetTick = m_ClientSimulationSystemGroup.ServerTick;
            if (m_ClientSimulationSystemGroup.ServerTickFraction < 1)
                targetTick--;

            if (targetTick == m_LastServerTick)
                return;

            m_LastServerTick = targetTick;

            using (var chunks = m_IncomingDataQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                foreach (var chunk in chunks)
                {
                    OnChunkProcess(chunk, NetworkTimeSystem.TimestampMS, targetTick);
                }
            }
        }

        private void OnChunkProcess(ArchetypeChunk chunk, in uint localTime, in uint targetTick)
        {
            var connectionArray  = chunk.GetNativeArray(GetArchetypeChunkComponentType<NetworkStreamConnection>(true));
            var snapshotAckArray = chunk.GetNativeArray(GetArchetypeChunkComponentType<NetworkSnapshotAckComponent>(true));
            var targetArray      = chunk.GetNativeArray(GetArchetypeChunkComponentType<CommandTargetComponent>(true));
            for (var ent = 0; ent < chunk.Count; ent++)
            {
                var target = targetArray[ent];
                if (target.targetEntity == default)
                    continue;

                var ack    = snapshotAckArray[ent];
                var writer = new DataStreamWriter(512, Allocator.Temp);
                writer.Write((byte) NetworkStreamProtocol.Command);
                writer.Write(ack.LastReceivedSnapshotByLocal);
                writer.Write(localTime);

                uint returnTime = ack.LastReceivedRemoteTime;
                if (returnTime != 0)
                    returnTime -= localTime - ack.LastReceiveTimestamp;
                writer.Write(returnTime);
                writer.Write(targetTick);

                foreach (var systemKvp in m_CommandCollectionSystem.SystemProcessors)
                {
                    writer.Write((byte) systemKvp.Key);
                
                    systemKvp.Value.BeginSerialize(target.targetEntity, systemKvp.Key);
                    systemKvp.Value.ProcessSend(targetTick, writer, m_NetworkCompressionModel);
                }
                
                writer.Flush();

                var driver = m_ReceiveSystem.Driver;
                driver.Send(m_ReceiveSystem.UnreliablePipeline, connectionArray[ent].Value, writer);
            }
        }
    }
}
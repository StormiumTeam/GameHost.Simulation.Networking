using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    // dependency just for acking
    [UpdateAfter(typeof(NetworkReceiveSnapshotSystemGroup))]
    public sealed class CommandSendSystem : ComponentSystem
    {
        private EntityQuery                m_IncomingDataQuery;
        private CommandCollectionSystem    m_CommandCollectionSystem;
        private NetworkTimeSystem          m_TimeSystem;
        private NetworkStreamReceiveSystemGroup m_ReceiveSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IncomingDataQuery = GetEntityQuery(new EntityQueryDesc
            {
                All  = new ComponentType[] {typeof(CommandTargetComponent), typeof(NetworkStreamInGame)}
            });
            m_CommandCollectionSystem = World.GetOrCreateSystem<CommandCollectionSystem>();
            m_ReceiveSystem           = World.GetOrCreateSystem<NetworkStreamReceiveSystemGroup>();
            m_TimeSystem              = World.GetOrCreateSystem<NetworkTimeSystem>();
        }

        protected override void OnUpdate()
        {
            foreach (var system in m_CommandCollectionSystem.SystemProcessors.Values)
                system.Prepare();

            using (var chunks = m_IncomingDataQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                foreach (var chunk in chunks)
                {
                    OnChunkProcess(chunk, NetworkTimeSystem.TimestampMS, m_TimeSystem.predictTargetTick);
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
                var writer = new DataStreamWriter(256, Allocator.Temp);
                writer.Write((byte) NetworkStreamProtocol.Command);
                writer.Write(ack.LastReceivedSnapshotByLocal);
                writer.Write(localTime);
                writer.Write(ack.LastReceivedRemoteTime - (localTime - ack.LastReceiveTimestamp));

                foreach (var systemKvp in m_CommandCollectionSystem.SystemProcessors)
                {
                    systemKvp.Value.BeginSerialize(target.targetEntity, systemKvp.Key);
                    systemKvp.Value.ProcessSend(targetTick, writer);
                }

                m_ReceiveSystem.QueueData(PipelineType.Unreliable, connectionArray[ent].Value, writer);
            }
        }
    }
}
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

using UnityEngine.Profiling;

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
                All = new ComponentType[] {typeof(CommandTargetComponent), typeof(NetworkStreamInGame)},
                None = new ComponentType[] {typeof(NetworkStreamDisconnected), typeof(NetworkStreamRequestDisconnect)}
            });
            m_CommandCollectionSystem = World.GetOrCreateSystem<CommandCollectionSystem>();
            m_ReceiveSystem           = World.GetOrCreateSystem<NetworkStreamReceiveSystem>();
            m_ClientSimulationSystemGroup = World.GetOrCreateSystem<ClientSimulationSystemGroup>();

            m_NetworkCompressionModel = new NetworkCompressionModel(Allocator.Persistent);
        }

        protected override void OnUpdate()
        {
            Profiler.BeginSample("Prepare()");
            foreach (var system in m_CommandCollectionSystem.SystemProcessors.Values)
                system.Prepare();
            Profiler.EndSample();

            var targetTick = m_ClientSimulationSystemGroup.ServerTick;
            if (m_ClientSimulationSystemGroup.ServerTickFraction < 1)
                targetTick--;

            if (targetTick == m_LastServerTick)
                return;

            m_LastServerTick = targetTick;

            Profiler.BeginSample("OnChunkProcess");
            using (var chunks = m_IncomingDataQuery.CreateArchetypeChunkArray(Allocator.TempJob))
            {
                foreach (var chunk in chunks)
                {
                    OnChunkProcess(chunk, NetworkTimeSystem.TimestampMS, targetTick);
                }
            }
            Profiler.EndSample();
        }

        private void OnChunkProcess(ArchetypeChunk chunk, in uint localTime, in uint targetTick)
        {
            var connectionArray  = chunk.GetNativeArray(GetComponentTypeHandle<NetworkStreamConnection>(true));
            var snapshotAckArray = chunk.GetNativeArray(GetComponentTypeHandle<NetworkSnapshotAckComponent>(true));
            var targetArray      = chunk.GetNativeArray(GetComponentTypeHandle<CommandTargetComponent>(true));
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
                
                Profiler.BeginSample("BeginSerialize and ProcessSend");
                foreach (var systemKvp in m_CommandCollectionSystem.SystemProcessors)
                {
                    writer.Write((byte) systemKvp.Key);
                
                    Profiler.BeginSample("BeginSerialize");
                    systemKvp.Value.BeginSerialize(target.targetEntity, systemKvp.Key);
                    Profiler.EndSample();
                    Profiler.BeginSample("ProcessSend");
                    systemKvp.Value.ProcessSend(targetTick, writer, m_NetworkCompressionModel);
                    Profiler.EndSample();
                }
                Profiler.EndSample();
                
                writer.Flush();

                Profiler.BeginSample("Send");
                var driver = m_ReceiveSystem.Driver;
                driver.Send(m_ReceiveSystem.UnreliablePipeline, connectionArray[ent].Value, writer);
                Profiler.EndSample();
            }
        }
    }
}
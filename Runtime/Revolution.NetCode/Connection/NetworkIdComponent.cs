using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
    public struct NetworkIdComponent : IComponentData
    {
        public int Value;
    }

    internal struct RpcSetNetworkId : IRpcCommandRequestComponentData
    {
        public int nid;

        public void Serialize(DataStreamWriter writer)
        {
            writer.Write(nid);
        }

        public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            nid = reader.ReadInt(ref ctx);
        }

        public Entity SourceConnection { get; set; }
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public class SetNetworkIdSystem : JobComponentSystem
    {
        private struct AddJob : IJobForEachWithEntity_EC<RpcSetNetworkId>
        {
            public EntityCommandBuffer.Concurrent Ecb;

            public void Execute(Entity reqEnt, int i, ref RpcSetNetworkId req)
            {
                Ecb.AddComponent(i, req.SourceConnection, new NetworkIdComponent {Value = req.nid});
                Ecb.DestroyEntity(i, reqEnt);
            }
        }

        private EndSimulationEntityCommandBufferSystem m_EndBarrier;

        protected override void OnCreate()
        {
            m_EndBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            return new AddJob
            {
                Ecb = m_EndBarrier.CreateCommandBuffer().ToConcurrent()
            }.Schedule(this, inputDeps);
        }
    }
}
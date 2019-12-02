using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Transforms;

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
        private EndSimulationEntityCommandBufferSystem m_EndBarrier;

        protected override void OnCreate()
        {
            m_EndBarrier = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var ecb = m_EndBarrier.CreateCommandBuffer().ToConcurrent();
            inputDeps = Entities.ForEach((Entity reqEnt, int nativeThreadIndex, in RpcSetNetworkId req) =>
            {
                ecb.AddComponent(nativeThreadIndex, req.SourceConnection, new NetworkIdComponent {Value = req.nid});
                ecb.DestroyEntity(nativeThreadIndex, reqEnt);
            }).Schedule(inputDeps);

            m_EndBarrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }
    }
}
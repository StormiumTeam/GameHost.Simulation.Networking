using Unity.Entities;

namespace package.stormiumteam.networking.ecs
{
    public struct BroadcastEntityComponentsOnce : IComponentData
    {
        public int NetInstanceId;

        public BroadcastEntityComponentsOnce(int id)
        {
            NetInstanceId = id;
        }
    }

    public struct BroadcastEntityComponentsThenDestroyIt : IComponentData
    {
        
    }
}
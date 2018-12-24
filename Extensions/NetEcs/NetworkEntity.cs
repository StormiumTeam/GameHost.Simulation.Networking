using Unity.Entities;

namespace package.stormiumteam.networking.extensions.NetEcs
{
    public struct NetworkEntity
    {
        public int InstanceId;
        public Entity Source;
    }
}
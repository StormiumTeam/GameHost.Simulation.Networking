using Unity.Entities;

namespace Revolution.NetCode
{
    public struct CommandTargetComponent : IComponentData
    {
        public Entity targetEntity;
    }
}
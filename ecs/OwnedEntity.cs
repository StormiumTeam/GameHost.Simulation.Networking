using Unity.Entities;

namespace package.stormiumteam.networking.ecs
{
    
    /// <summary>
    /// Automatically managed.
    /// </summary>
    /// <remarks>
    /// If <see cref="OwnedEntityData"/> is present, then this component is also present
    /// </remarks>
    public struct WeOwnThisEntity : IComponentData
    {

    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// If <see cref="WeOwnThisEntity"/> is present, then this component is also present
    /// </remarks>
    public struct OwnedEntityData : IComponentData
    {
        public NetUser Owner;

        public OwnedEntityData(NetUser user)
        {
            Owner = user;
        }
    }
}
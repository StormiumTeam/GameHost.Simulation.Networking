using Unity.Entities;

namespace package.stormiumteam.networking
{
    public struct UserRelativeConnectionData : IComponentData
    {
        public ConnectionType ConnectionType;
    }
}
using LiteNetLib;
using package.stormiumteam.networking;
using Unity.Entities;

namespace package.stormiumteam.networking.game
{
    public struct PlayerUserLink : ISharedComponentData
    {
        public NetUser Target;

        public PlayerUserLink(NetUser target)
        {
            Target = target;
        }

        public PlayerUserLink(int instanceId, ulong id)
        {
            Target = new NetUser(instanceId, id);
        }
        
        public PlayerUserLink(NetworkInstance instance, ulong id)
        {
            Target = new NetUser(instance, id);
        }
    }

    public struct PlayerPeerLink : ISharedComponentData
    {
        public NetPeerInstance Owner;
        public NetPeer Target;

        public PlayerPeerLink(NetPeerInstance owner, NetPeer target)
        {
            Owner = owner;
            Target = target;
        }
    }

    public struct ClientPlayerServerPlayerLink : IComponentData
    {
        public int NetTarget;

        public ClientPlayerServerPlayerLink(int target)
        {
            NetTarget = target;
        }
    }
}
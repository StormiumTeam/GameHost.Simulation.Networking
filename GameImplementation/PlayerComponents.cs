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

        public PlayerUserLink(int peerId, int instanceId, ulong id)
        {
            Target = new NetUser(peerId, instanceId, id);
        }
        
        public PlayerUserLink(NetPeerInstance peerInstance, NetworkInstance instance, ulong id)
        {
            Target = new NetUser(peerInstance, instance, id);
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
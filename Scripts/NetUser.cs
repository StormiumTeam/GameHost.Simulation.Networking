using System;
using System.Collections.Generic;
using Unity.Entities;

namespace package.stormiumteam.networking
{
    [Serializable]
    public struct NetUser
    {
        public int PeerId;
        public int OwnerId;
        
        public readonly ulong Index;

        public NetUser(NetPeerInstance peerInstance, NetworkInstance owner, ulong index)
        {
            PeerId = peerInstance.Id;
            OwnerId = owner.Id;
            Index = index;
        }
        
        public NetUser(int peerId, int ownerId, ulong index)
        {
            PeerId = peerId;
            OwnerId = ownerId;
            Index   = index;
        }

        public static bool operator ==(NetUser l, NetUser r) => l.Index == r.Index;

        public static bool operator !=(NetUser l, NetUser r) => !(l == r);

        public override bool Equals(object obj)
        {
            if (!(obj is NetUser)) return base.Equals(obj);
            
            var asUser = (NetUser) obj;
            return this == asUser;

        }

        public override string ToString()
        {
            return $"user#{Index}(peer: {PeerId}, owner: {OwnerId})";
        }

        public bool Equals(NetUser other)
        {
            return Index == other.Index;
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }

        public NetworkInstance GetOwner()
        {
            return NetworkInstance.FromId(OwnerId);
        }
        
        public NetPeerInstance GetPeerInstance()
        {
            return NetPeerInstance.FromId(PeerId);
        }
    }
}
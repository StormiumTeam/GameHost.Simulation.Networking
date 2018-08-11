using System;
using System.Collections.Generic;
using Unity.Entities;

namespace package.stormiumteam.networking
{
    [Serializable]
    public struct NetUser
    {
        public int OwnerId;
        
        public readonly ulong Index;

        public NetUser(NetworkInstance owner, ulong index)
        {
            OwnerId = owner.Id;
            Index = index;
        }
        
        public NetUser(int ownerId, ulong index)
        {
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

        public bool Equals(NetUser other)
        {
            return Index == other.Index;
        }

        public override int GetHashCode()
        {
            return Index.GetHashCode();
        }
    }
}
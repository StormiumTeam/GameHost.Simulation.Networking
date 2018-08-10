using System;
using System.Collections.Generic;
using Unity.Entities;

namespace package.stormiumteam.networking
{
    [Serializable]
    public struct NetUser
    {
        public NetworkInstance Owner;
        
        public readonly ulong Index;

        public NetUser(NetworkInstance owner, ulong index)
        {
            Owner = owner;

            Index = index;
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
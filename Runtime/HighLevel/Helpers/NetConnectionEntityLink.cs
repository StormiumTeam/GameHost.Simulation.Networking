using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public static class NetConnectionEntityLink
    {
        private static readonly NativeHashMap<int, Entity> EntitiesLink;

        static NetConnectionEntityLink()
        {
            EntitiesLink = new NativeHashMap<int, Entity>(16, Allocator.Persistent);
            Application.quitting += () => EntitiesLink.Dispose();
        }
        
        public static bool TryGetEntity(NetworkConnection connection, out Entity entity)
        {
            return TryGetEntity(connection.Id, out entity);
        }
        
        public static bool TryGetEntity(int id, out Entity entity)
        {
            return EntitiesLink.TryGetValue(id, out entity);
        }
        
        public static int TrySetEntity(NetworkConnection connection, Entity entity)
        {
            return TrySetEntity(connection.Id, entity);
        }

        public static int TrySetEntity(int id, Entity entity)
        {
            Entity _;
            if (!TryGetEntity(id, out _))
            {
                return EntitiesLink.TryAdd(id, entity) ? 0 : 1;
            }

            return -1;
        }
    }
}
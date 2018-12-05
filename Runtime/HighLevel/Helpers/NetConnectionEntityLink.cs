using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public static class NetConnectionEntityLink
    {
        private static NativeHashMap<int, Entity> s_EntitiesLink;

        static NetConnectionEntityLink()
        {
            s_EntitiesLink = new NativeHashMap<int, Entity>(16, Allocator.Persistent);
            Application.quitting += () => s_EntitiesLink.Dispose();
        }
        
        public static bool TryGetEntity(NetworkConnection connection, out Entity entity)
        {
            return TryGetEntity(connection.Id, out entity);
        }
        
        public static bool TryGetEntity(int id, out Entity entity)
        {
            return s_EntitiesLink.TryGetValue(id, out entity);
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
                return s_EntitiesLink.TryAdd(id, entity) ? 0 : 1;
            }

            return -1;
        }
    }
}
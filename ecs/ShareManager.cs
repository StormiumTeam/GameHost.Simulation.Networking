using LiteNetLib;
using Unity.Entities;

namespace package.stormiumteam.networking.ecs
{
    public static class ShareManager
    {
        public static EntityManager MainEntityMgr => World.Active.GetExistingManager<EntityManager>();
        
        public static void SetShareOption(Entity entity, ComponentType type, ComponentShareOption option, DeliveryMethod deliver)
        {
            if (!MainEntityMgr.HasComponent(entity, typeof(NetworkEntityComponentSettingsBuffer)))
            {
                MainEntityMgr.AddBuffer<NetworkEntityComponentSettingsBuffer>(entity);
            }

            var typeIndex = type.TypeIndex;
            var buffer    = MainEntityMgr.GetBuffer<NetworkEntityComponentSettingsBuffer>(entity);
            var key       = -1;
            for (int i = 0; key == -1 && i != buffer.Length; i++)
            {
                if (buffer[i].ServerTypeIndex != typeIndex) continue;

                key = i;
            }

            var value = new NetworkEntityComponentSettingsBuffer(typeIndex, option, deliver);
            if (key == -1)
                buffer.Add(value);
            else
                buffer[key] = value;
        }
    }
} 
using System;
using System.Diagnostics;
using LiteNetLib;
using package.stormiumteam.networking.plugins;
using Unity.Entities;

namespace package.stormiumteam.networking.ecs
{
    public struct NetworkEntityComponentSettingsBuffer : IBufferElementData
    {
        public int                  ServerTypeIndex;
        public ComponentShareOption ShareOption;
        public DeliveryMethod       DeliveryMethod;

        public NetworkEntityComponentSettingsBuffer(int serverTypeIndex, ComponentShareOption shareOption, DeliveryMethod deliveryMethod)
        {
            ServerTypeIndex = serverTypeIndex;
            ShareOption     = shareOption;
            DeliveryMethod  = deliveryMethod;
        }

        public Type GetManagedType(NetworkInstance instance)
        {
            Debug.Assert(ServerTypeIndex <= 0, "ServerTypeIndex <= 0");

            return instance.Get<ConnectionTypeManager>().GetType(ServerTypeIndex);
        }
    }
}
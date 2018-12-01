using System;
using Unity.Collections.LowLevel.Unsafe;

namespace ENet
{
    public struct NativeNetHost
    {
        [NativeDisableUnsafePtrRestriction]
        public IntPtr NativeHost;
        
        public NativeNetHost(Host host)
        {
            NativeHost = host.NativeData;
        }
        
        public NativeNetHost(IntPtr ptr)
        {
            NativeHost = ptr;
        }
        
        public void Broadcast(byte channelID, ref Packet packet) {
            CheckCreated();

            packet.CheckCreated();
            Native.enet_host_broadcast(NativeHost, channelID, packet.NativeData);
            packet.NativeData = IntPtr.Zero;
        }

        public int Service(out Event @event) {
            CheckCreated();

            ENetEvent nativeEvent;

            var result = Native.enet_host_service(NativeHost, out nativeEvent, 0);
            
            if (result <= 0) {
                @event = new Event();

                return result;
            }

            @event = new Event(nativeEvent);

            return result;
        }

        public void CheckCreated()
        {
            if (NativeHost == IntPtr.Zero)
                throw new InvalidOperationException("Host not created");
        }
    }
}
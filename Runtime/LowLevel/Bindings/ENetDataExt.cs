using System;
using Unity.Collections.LowLevel.Unsafe;

namespace ENet
{
    public struct NativeENetHost : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        public IntPtr NativeHost;
        
        public NativeENetHost(Host host)
        {
            NativeHost = host.NativeData;
        }
        
        public NativeENetHost(IntPtr ptr)
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
        
        public void Flush() {
            CheckCreated();

            Native.enet_host_flush(NativeHost);
        }

        public void Dispose()
        {
            if (NativeHost == IntPtr.Zero) return;
            
            Native.enet_host_destroy(NativeHost);
            NativeHost = IntPtr.Zero;
        }
    }
}
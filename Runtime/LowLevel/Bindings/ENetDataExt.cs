using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace ENet
{
    public struct NativeENetHost : IDisposable
    {
        [NativeDisableUnsafePtrRestriction]
        private IntPtr m_NativeHost;

        public IntPtr NativeHost
        {
            get { return m_NativeHost; }
            set { m_NativeHost = value; }
        }

        public NativeENetHost(Host host)
        {
            m_NativeHost = host.NativeData;
        }
        
        public NativeENetHost(IntPtr ptr)
        {
            m_NativeHost = ptr;
        }
        
        public void Broadcast(byte channelID, ref Packet packet) {
            CheckCreated();

            packet.CheckCreated();
            Native.enet_host_broadcast(m_NativeHost, channelID, packet.NativeData);
            packet.NativeData = IntPtr.Zero;
        }

        public int Service(out Event @event) {
            CheckCreated();

            ENetEvent nativeEvent;

            var result = Native.enet_host_service(m_NativeHost, out nativeEvent, 0);
            
            if (result <= 0) {
                @event = new Event();
                
                if (result < 0)
                    Debug.LogError("Failure");

                return result;
            }

            @event = new Event(nativeEvent);

            return result;
        }

        public void CheckCreated()
        {
            if (m_NativeHost == IntPtr.Zero)
                throw new InvalidOperationException("Host not created");
        }
        
        public void Flush() {
            CheckCreated();

            Native.enet_host_flush(m_NativeHost);
        }

        public void Dispose()
        {
            if (m_NativeHost == IntPtr.Zero) return;
            
            Native.enet_host_destroy(m_NativeHost);
            m_NativeHost = IntPtr.Zero;
        }
    }
}
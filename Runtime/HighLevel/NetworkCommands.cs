#define NETWORKING_ENET

using System;
using ENet;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Mathematics;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public unsafe struct NetworkCommands
    {
#if NETWORKING_ENET
        private Peer           m_ENetPeer;
        private NativeENetHost m_ENetHost;
        private IntPtr         m_ENetData;

        private byte m_IsPeer;
#endif
        public bool IsPeer => m_IsPeer == 1;

        public NetworkCommands(Type type, IntPtr data)
        {
            Debug.Log(type.Name + ", " + data.ToString());

#if NETWORKING_ENET
            m_ENetPeer = default(Peer);
            m_ENetHost = default(NativeENetHost);
            m_ENetData = data;

            m_IsPeer = (byte) (type == typeof(Peer) ? 1 : 0);

            if (m_IsPeer == 1) m_ENetPeer = new Peer(data);
            else m_ENetHost               = new NativeENetHost(data);
#endif
        }

        public NetworkCommands(byte isPeer, IntPtr data)
        {
#if NETWORKING_ENET
            m_ENetPeer = default(Peer);
            m_ENetHost = default(NativeENetHost);
            m_ENetData = data;

            m_IsPeer = isPeer;

            if (m_IsPeer == 1) m_ENetPeer = new Peer(data);
            else m_ENetHost               = new NativeENetHost(data);
#endif
        }

        /// <summary>
        /// Send a packet to the instance. If it's a host (local), it will be broadcasted to everyone.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="channel"></param>
        /// <param name="delivery"></param>
        /// <returns></returns>
        public bool Send(DataBufferWriter buffer, NetworkChannel channel, Delivery delivery)
        {
            return Send(buffer.GetSafePtr(), buffer.Length, channel, delivery);
        }

        /// <summary>
        /// Send a packet to the instance. If it's a host (local), it will be broadcasted to everyone.
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="length"></param>
        /// <param name="channel"></param>
        /// <param name="delivery"></param>
        /// <returns></returns>
        public bool Send(void* ptr, int length, NetworkChannel channel, Delivery delivery)
        {
            return Send(new IntPtr(ptr), length, channel, delivery);
        }

        /// <summary>
        /// Send a packet to the instance. If it's a host (local), it will be broadcasted to everyone.
        /// </summary>
        /// <param name="ptr"></param>
        /// <param name="length"></param>
        /// <param name="channel"></param>
        /// <param name="delivery"></param>
        /// <returns></returns>
        public bool Send(IntPtr ptr, int length, NetworkChannel channel, Delivery delivery)
        {
            var packet = new Packet();
            packet.Create(ptr, length, delivery.ToENetPacketFlags());

            if (m_IsPeer == 1)
            {
                return m_ENetPeer.Send(channel.Id, ref packet);
            }

            m_ENetHost.Broadcast(channel.Id, ref packet);
            return true;
        }

        public ulong BytesSent =>
            math.select((uint) Native.enet_peer_get_bytes_sent(m_ENetData), Native.enet_host_get_bytes_sent(m_ENetData), !IsPeer);

        public ulong BytesReceived =>
            math.select((uint) Native.enet_peer_get_bytes_received(m_ENetData), Native.enet_host_get_bytes_received(m_ENetData), !IsPeer);
    }
}
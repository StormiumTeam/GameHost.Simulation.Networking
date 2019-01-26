using System;
using System.Diagnostics;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using Valve.Sockets;
using Debug = UnityEngine.Debug;

namespace package.stormiumteam.networking.runtime.highlevel
{
    [Flags]
    public enum Delivery
    {
        Error       = 0,
        Unreliable  = 1 << 0,
        Reliable    = Unreliable << 1,
        Unsequenced = Reliable << 1,
    }

    public static class DeliveryExtensions
    {
        public static SendType ToGnsSendTypeFlags(this Delivery delivery)
        {
            return (delivery & Delivery.Reliable) != 0 ? SendType.Reliable : SendType.Unreliable;
        }
    }

    public unsafe struct NetworkHost
    {
        public IntPtr NativePtr;
        public NetworkConnection HostConnection;
        public uint Socket;

        public NetworkHost(NetworkConnection hostConnection, IntPtr native, uint socket)
        {
            NativePtr = native;
            HostConnection = hostConnection;
            Socket = socket;
        }

        [BurstDiscard]
        public int GetNextEvent(ref NetworkEvent networkEvent)
        {
            var foreignConnection = default(NetworkConnection);
            var foreignCmds = default(NetworkCommands);
            var code = 0;
#if NETWORKING_ENET
            Event ev;
            code = Native.Service(out ev);

            Debug.Log($"code={code}, type={ev.Type}");
            
            if (ev.Type == EventType.None)
            {
                networkEvent = new NetworkEvent(NetworkEventType.None, foreignConnection, foreignCmds);
                
                return code;
            }

            var enetPeerConnection = default(ENetPeerConnection);
            var peer = ev.Peer;
            if (peer.IsSet)
            {
                ENetPeerConnection.GetOrCreate(peer, out enetPeerConnection);
                
                foreignCmds = new NetworkCommands(1, peer.NativeData);
                
                peer.Timeout(3, 1000, 5000);
            }

            if (enetPeerConnection.IsCreated)
            {
                foreignConnection = enetPeerConnection.Connection;
                foreignConnection.ParentId = HostConnection.Id;
                enetPeerConnection.Connection = foreignConnection;
            }

            switch (ev.Type)
            {
                case EventType.Connect:
                {
                    networkEvent = new NetworkEvent(NetworkEventType.Connected, foreignConnection, foreignCmds)
                    {
                        ConnectionData = ev.Data
                    };
                    break;
                }
                case EventType.Disconnect:
                {
                    networkEvent = new NetworkEvent(NetworkEventType.Disconnected, foreignConnection, foreignCmds)
                    {
                        TimeoutForeignDisconnection = 0
                    };

                    peer.Data = IntPtr.Zero;
                    
                    break;
                }
                case EventType.Receive:
                {
                    networkEvent = new NetworkEvent(NetworkEventType.DataReceived, foreignConnection, foreignCmds);
                    networkEvent.SetData((byte*) ev.Packet.Data, ev.Packet.Length);
                    break;
                }
                case EventType.Timeout:
                {
                    networkEvent = new NetworkEvent(NetworkEventType.Disconnected, foreignConnection, foreignCmds)
                    {
                        TimeoutForeignDisconnection = 1
                    };
                    
                    peer.Data = IntPtr.Zero;
                    
                    break;
                }
                default:
                {
                    networkEvent = default(NetworkEvent);
                    break;
                }
            }
#endif
            return code;
        }

        public void BatchSend(DataBufferWriter buffer, Delivery delivery, in NativeArray<uint> connections, ref NativeArray<Result> results)
        {
            if (delivery == Delivery.Error) throw new InvalidOperationException("Invalid delivery type");

            for (var i = 0; i != connections.Length; i++)
            {
                var con = connections[i];
                var bufferPtr = buffer.GetSafePtr();
                var bufferCnt = (uint) buffer.Length;
                var sendType = delivery.ToGnsSendTypeFlags();
                
                Native.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(NativePtr, con, bufferPtr, bufferCnt, sendType);
            }
#if NETWORKING_ENET
            var packet = new Packet();
            
            packet.Create(buffer.GetSafePtr(), buffer.Length, delivery.ToENetPacketFlags());
            
            Native.Broadcast(channel, ref packet);
#endif
        }
    }
}
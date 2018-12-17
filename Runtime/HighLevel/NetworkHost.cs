#define NETWORKING_ENET

using System;
using System.Diagnostics;
using ENet;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
        public static PacketFlags ToENetPacketFlags(this Delivery delivery)
        {
            var flags = default(PacketFlags);
            
            if ((delivery & Delivery.Reliable) != 0 && (delivery & Delivery.Unreliable) == 0) 
                flags |= PacketFlags.Reliable;
            if ((delivery & Delivery.Unsequenced) != 0) 
                flags |= PacketFlags.Unsequenced;

            return flags;
        }
    }

    public unsafe struct NetworkHost : IDisposable
    {
#if NETWORKING_ENET
        public NativeENetHost Native;
#endif
        public NetworkConnection HostConnection;

        public NetworkHost(NetworkConnection hostConnection, IntPtr data)
        {
#if NETWORKING_ENET
            Native = new NativeENetHost(data);
#endif
            
            HostConnection = hostConnection;
        }

        public int GetNextEvent(ref NetworkEvent networkEvent)
        {
            var foreignConnection = default(NetworkConnection);
            var foreignCmds = default(NetworkCommands);
            var code = 0;
#if NETWORKING_ENET
            Event ev;
            code = Native.Service(out ev);

            if (ev.Type == EventType.None)
            {
                networkEvent = new NetworkEvent(NetworkEventType.None, foreignConnection, foreignCmds);
                
                return code;
            }

            var enetPeerConnection = default(ENetPeerConnection);
            var peer = ev.Peer;
            if (peer.IsSet)
            {
                if (!ENetPeerConnection.GetOrCreate(peer, out enetPeerConnection))
                {
                    enetPeerConnection.Connection = NetworkConnection.New(HostConnection.Id);
                }
                
                foreignCmds = new NetworkCommands(1, peer.NativeData);
            }

            if (enetPeerConnection.IsCreated)
            {
                foreignConnection = enetPeerConnection.Connection;
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
                    break;
                }
                case EventType.Receive:
                {
                    var dataArray = new NativeArray<byte>(ev.Packet.Length, Allocator.Temp);
                    UnsafeUtility.MemCpy(dataArray.GetUnsafePtr(), (void*) ev.Packet.Data, ev.Packet.Length);
                    
                    networkEvent = new NetworkEvent(NetworkEventType.DataReceived, foreignConnection, foreignCmds);
                    networkEvent.SetData(dataArray);
                    break;
                }
                case EventType.Timeout:
                {
                    networkEvent = new NetworkEvent(NetworkEventType.Disconnected, foreignConnection, foreignCmds)
                    {
                        TimeoutForeignDisconnection = 1
                    };
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

        public void Broadcast(DataBufferWriter buffer, Delivery delivery, byte channel = 0)
        {
            if (delivery == Delivery.Error) throw new InvalidOperationException("Invalid delivery type");
            
#if NETWORKING_ENET
            var packet = new Packet();
            
            packet.Create(buffer.GetSafePtr(), buffer.Buffer.Length, delivery.ToENetPacketFlags());
            
            Native.Broadcast(channel, ref packet);
#endif
        }

        public void Flush()
        {
            Native.Flush();
        }
        
        public void Dispose()
        {
            Native.Dispose();
        }
    }
}
using System;
using System.Net;
using ENet;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

namespace package.stormiumteam.networking.Tests
{
    public class CreateClientCode
    {
        public int ServerPort;

        private JobHandle                      m_ClientHandle;
        private NetDriver                      m_Driver;
        private NativeArray<NetworkConnection> m_Connection;
        private NativeHashMap<uint, IntPtr>    m_MapPeers;

        struct ClientJobEvent : IJob
        {
            public NativeENetHost Host;

            public void Execute()
            {
                Event netEvent;
                while (Host.Service(out netEvent) > 0)
                {
                    switch (netEvent.Type)
                    {
                        case EventType.Connect:
                            Debug.Log("Client - Connected to server.");

                            using (var writer = new DataBufferWriter(Allocator.Temp))
                            {
                                writer.WriteStatic("Hello server!");

                                var packet = default(Packet);
                                packet.Create(writer.GetSafePtr(), writer.Buffer.Length, PacketFlags.Reliable);

                                netEvent.Peer.Send(0, ref packet);
                            }

                            break;
                        case EventType.None:
                            break;
                        case EventType.Disconnect:
                            break;
                        case EventType.Receive:
                            break;
                        case EventType.Timeout:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        public void Start()
        {
            // Client
            m_Connection = new NativeArray<NetworkConnection>(1, Allocator.Persistent);
            m_Driver     = new NetDriver(m_Connection.Length);
            if (m_Driver.Bind(new Address()) != NetDriverBindError.Success)
                Debug.Log("Failed to bind to any port, reason: ");
            else
            {
                //m_Connection[0] = m_Driver.Connect(new IPEndPoint(IPAddress.Loopback, ServerPort));
            }
        }

        public void Update()
        {
            m_ClientHandle.Complete();

            var clientUpdateJob = new ClientJobEvent
            {
                Host = new NativeENetHost(m_Driver.Host)
            };

            m_ClientHandle = clientUpdateJob.Schedule();
        }

        public void Destroy()
        {
            m_ClientHandle.Complete();
            m_Connection.Dispose();
        }
    }
}
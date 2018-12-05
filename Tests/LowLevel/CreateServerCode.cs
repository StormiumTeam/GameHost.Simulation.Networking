using System;
using System.Net;
using ENet;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

namespace package.stormiumteam.networking.Tests
{
    public class CreateServerCode
    {
        public int Port;

        private JobHandle                     m_ServerJobHandle;
        private NetDriver                     m_Driver;
        private NativeList<NetworkConnection> m_Connections;
        private NativeHashMap<uint, IntPtr>   m_MapPeers;

        private struct ServerUpdateJob : IJob
        {
            public NativeENetHost                 Host;
            public NativeList<NetworkConnection> Connections;

            public void Execute()
            {
                /*Event netEvent;
                while (Host.Service(out netEvent) > 0)
                {
                    switch (netEvent.Type)
                    {
                        case EventType.Connect:
                            Debug.Log($"Server - Client connected {netEvent.Peer.ID}");
                            Connections.Add(new NetworkConnection {Peer = netEvent.Peer});
                            break;
                        case EventType.Receive:
                            Debug.Log("Server - Receive data");
                            var reader = new DataBufferReader(netEvent.Packet.Data, netEvent.Packet.Length);
                            var str    = reader.ReadString();

                            Debug.Log(" > " + str);
                            break;
                        case EventType.None:
                            break;
                        case EventType.Disconnect:
                            break;
                        case EventType.Timeout:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                */
            }
        }

        private struct ServerRemoveOfflinePeersJob : IJob
        {
            public NativeList<NetworkConnection> Connections;

            public void Execute()
            {
                for (int index = 0; index != Connections.Length; index++)
                {
                    /*var peer     = Connections[index].Peer;
                    var intState = (int) peer.State;

                    // The connection is currently connected, no need to worry
                    if (intState >= 1 && intState <= 5 && peer.NativeData != IntPtr.Zero)
                        return;

                    Connections.RemoveAtSwapBack(index);*/
                }
            }
        }

        private struct ServerUpdatePeersHashMapJob : IJob
        {
            [ReadOnly]
            public NativeList<NetworkConnection> Connections;

            [NativeDisableUnsafePtrRestriction]
            public NativeHashMap<uint, IntPtr>.Concurrent MapPeers;

            public void Execute()
            {
                /*for (int index = 0; index != Connections.Length; index++)
                {
                    MapPeers.TryAdd(Connections[index].Peer.ID, Connections[index].Peer.NativeData);
                }*/
            }
        }

        public void Start()
        {
            // Server
            m_Connections = new NativeList<NetworkConnection>(32, Allocator.Persistent);
            m_MapPeers    = new NativeHashMap<uint, IntPtr>(32, Allocator.Persistent);

            m_Driver = new NetDriver(32);
            if (m_Driver.Bind(new IPEndPoint(IPAddress.Any, Port)) != NetDriverBindError.Success)
                Debug.Log($"Failed to bind to port {Port}, reason: ");
            else
            {
                m_Driver.Listen();
                Debug.Log($"Listening for any connection on port {Port}...");
            }
        }

        public void Update()
        {
            m_ServerJobHandle.Complete();

            // Clear the peers list
            m_MapPeers.Clear();

            var updateJob = new ServerUpdateJob
            {
                Host        = new NativeENetHost(m_Driver.Host),
                Connections = m_Connections
            };

            var removeOfflinePeersJob = new ServerRemoveOfflinePeersJob
            {
                Connections = m_Connections
            };

            var updatePeersHashMapJob = new ServerUpdatePeersHashMapJob
            {
                Connections = m_Connections,
                MapPeers    = m_MapPeers.ToConcurrent()
            };

            m_ServerJobHandle = updateJob.Schedule();
            m_ServerJobHandle = removeOfflinePeersJob.Schedule();
            m_ServerJobHandle = updatePeersHashMapJob.Schedule();
        }

        public void Destroy()
        {
            m_ServerJobHandle.Complete();
            m_Connections.Dispose();
            m_MapPeers.Dispose();
        }
    }
}
using System;
using System.Collections.Generic;
using package.stormiumteam.networking.runtime;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace ENet
{
    public unsafe struct ENetPeerConnection
    {
        private void* m_Buffer;

        private NetworkConnection m_ConnectionBuffer;

        public NetworkConnection Connection
        {
            get
            {
                Check();

                ENetPeerConnection conBuffer;
                UnsafeUtility.CopyPtrToStructure(m_Buffer, out conBuffer);
                return conBuffer.m_ConnectionBuffer;
            }
            set
            {
                Check();

                ENetPeerConnection conBuffer;
                UnsafeUtility.CopyPtrToStructure(m_Buffer, out conBuffer);
                conBuffer.m_ConnectionBuffer = value;
                UnsafeUtility.CopyStructureToPtr(ref conBuffer, m_Buffer);
            }
        }

        public bool IsCreated => new IntPtr(m_Buffer) != IntPtr.Zero;

        private void Check()
        {
            if ((int) m_Buffer == 0) throw new InvalidOperationException("Null pointer");
        }

        public static ENetPeerConnection Allocate(Peer peer)
        {
            return Allocate(peer, NetworkConnection.New());
        }

        public static ENetPeerConnection Allocate(Peer peer, NetworkConnection netCon)
        {
            const Allocator label = Allocator.Persistent;

            var connectionData = new ENetPeerConnection();

            var buffer = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<ENetPeerConnection>(),
                UnsafeUtility.AlignOf<ENetPeerConnection>(), label);

            connectionData.m_Buffer   = buffer;
            connectionData.Connection = netCon;
            if (!s_Connections.TryAdd(netCon.Id, connectionData))
            {
                throw new InvalidOperationException();
            }

            peer.Data = new IntPtr(netCon.Id);

            return connectionData;
        }

        public static void Free(ENetPeerConnection connection)
        {
            Debug.Log("1");
            InternalFree(connection);
            Debug.Log("2");
            s_Connections.Remove(connection.Connection.Id);
            Debug.Log("3");
        }

        private static void InternalFree(ENetPeerConnection connection)
        {
            Debug.Log(">>>" + new IntPtr(connection.m_Buffer));
            UnsafeUtility.Free(connection.m_Buffer, Allocator.Persistent);
        }

        public static bool GetOrCreate(Peer peer, out ENetPeerConnection peerConnection)
        {
            if (peer.Data != IntPtr.Zero)
            {
                if (!s_Connections.TryGetValue(peer.Data.ToInt64(), out peerConnection))
                {
                    throw new InvalidOperationException($"peerId={peer.Data.ToInt64()}, peerConnection={new IntPtr(peerConnection.m_Buffer)}");
                }

                return true;
            }

            peerConnection = Allocate(peer);

            return false;
        }

        public static bool TryGet(int id, out ENetPeerConnection peerConnection)
        {
            return s_Connections.TryGetValue(id, out peerConnection);
        }

        private static NativeHashMap<long, ENetPeerConnection> s_Connections;

        static ENetPeerConnection()
        {
            Create();
        }

        [BurstDiscard]
        private static void Create()
        {
            s_Connections = new NativeHashMap<long, ENetPeerConnection>(32, Allocator.Persistent);


            PlayerLoopManager.RegisterDomainUnload(() =>
            {
                var length = NetworkConnection.Count;
                for (long i = 1; i != length; i++)
                {
                    ENetPeerConnection con;
                    if (!s_Connections.TryGetValue(i, out con)) continue;

                    InternalFree(con);
                }

                s_Connections.Dispose();
            }, 10001);
        }

        public static void StaticCreate()
        {
            // It will do nothing
        }
    }
}
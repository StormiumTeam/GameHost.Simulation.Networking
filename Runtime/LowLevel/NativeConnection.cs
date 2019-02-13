using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using Valve.Sockets;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public unsafe struct NativeConnection
    {
                private void* m_Buffer;

        private NetworkConnection m_ConnectionBuffer;

        public NetworkConnection Connection
        {
            get
            {
                Check();

                UnsafeUtility.CopyPtrToStructure(m_Buffer, out NativeConnection conBuffer);
                return conBuffer.m_ConnectionBuffer;
            }
            set
            {
                Check();

                UnsafeUtility.CopyPtrToStructure(m_Buffer, out NativeConnection conBuffer);
                conBuffer.m_ConnectionBuffer = value;
                UnsafeUtility.CopyStructureToPtr(ref conBuffer, m_Buffer);
            }
        }

        public bool IsCreated => new IntPtr(m_Buffer) != IntPtr.Zero;

        private void Check()
        {
            if ((int) m_Buffer == 0) throw new InvalidOperationException("Null pointer");
        }

        public static NativeConnection Allocate(IntPtr callerSocket, uint peerId)
        {
            return Allocate(callerSocket, peerId, NetworkConnection.New());
        }

        public static NativeConnection Allocate(IntPtr callerSocket, uint peerId, NetworkConnection netCon)
        {
            const Allocator label = Allocator.Persistent;

            var connectionData = new NativeConnection();

            var buffer = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<NativeConnection>(),
                UnsafeUtility.AlignOf<NativeConnection>(), label);

            connectionData.m_Buffer   = buffer;
            connectionData.Connection = netCon;
            if (!s_Connections.TryAdd(netCon.Id, connectionData))
            {
                throw new InvalidOperationException();
            }

            Native.SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(callerSocket, peerId, netCon.Id);
            
            Debug.Log($"Allocated {callerSocket} : {peerId} : {netCon}");

            return connectionData;
        }

        public static void Free(NativeConnection connection)
        {
            Debug.Log("1");
            InternalFree(connection);
            Debug.Log("2");
            s_Connections.Remove(connection.Connection.Id);
            Debug.Log("3");
        }

        private static void InternalFree(NativeConnection connection)
        {
            Debug.Log($"Freeing NativeConnection ptr={new IntPtr(connection.m_Buffer)} {connection.Connection.ToString()})");
            UnsafeUtility.Free(connection.m_Buffer, Allocator.Persistent);
        }

        public static bool GetOrCreate(IntPtr callerSocket, uint peerId, out NativeConnection nativeConnection)
        {
            var conData = Native.SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(callerSocket, peerId);
            
            if (conData != -1)
            {
                if (!s_Connections.TryGetValue(conData, out nativeConnection))
                {
                    throw new InvalidOperationException($"caller={callerSocket.ToInt64()}, peerId={peerId}, connectionData={conData}");
                }

                return true;
            }

            nativeConnection = Allocate(callerSocket, peerId);

            return false;
        }

        public static bool TryGet(long id, out NativeConnection peerConnection)
        {
            return s_Connections.TryGetValue(id, out peerConnection);
        }

        private static NativeHashMap<long, NativeConnection> s_Connections;

        static NativeConnection()
        {
            Create();
        }

        [BurstDiscard]
        private static void Create()
        {
            s_Connections = new NativeHashMap<long, NativeConnection>(32, Allocator.Persistent);


            PlayerLoopManager.RegisterDomainUnload(() =>
            {
                var length = NetworkConnection.Count;
                for (long i = 1; i != length; i++)
                {
                    NativeConnection con;
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
using System;
using System.Collections.Generic;
using package.stormiumteam.networking.runtime.lowlevel;
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

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle                                   m_Safety;
        [NativeSetClassTypeToNullOnSchedule] DisposeSentinel m_DisposeSentinel;
#endif

        private void Check()
        {
            if ((int) m_Buffer == 0) throw new InvalidOperationException("Null pointer");
        }

        public static ENetPeerConnection Allocate(Peer peer)
        {
            const Allocator label = Allocator.Persistent;

            var connectionData = new ENetPeerConnection();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out connectionData.m_Safety, out connectionData.m_DisposeSentinel, 1, label);
#endif
            var buffer = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<ENetPeerConnection>(),
                UnsafeUtility.AlignOf<ENetPeerConnection>(), Allocator.Persistent);
            var counter = s_Counter++;

            connectionData.m_Buffer = buffer;
            s_Connections[counter]  = connectionData;
            peer.Data               = new IntPtr(counter);

            return connectionData;
        }

        public static void Free(ENetPeerConnection connection)
        {
            InternalFree(connection);

            s_Connections.Remove(connection.Connection.Id);
        }
        
        private static void InternalFree(ENetPeerConnection connection)
        {
            UnsafeUtility.Free(connection.m_Buffer, Allocator.Persistent);
            DisposeSentinel.Dispose(ref connection.m_Safety, ref connection.m_DisposeSentinel);
        }

        public static bool GetOrCreate(Peer peer, out ENetPeerConnection peerConnection)
        {
            if (peer.Data != IntPtr.Zero)
            {
                peerConnection = s_Connections[peer.Data.ToInt64()];

                return true;
            }

            peerConnection = Allocate(peer);

            return false;
        }

        private static long                                 s_Counter;
        private static Dictionary<long, ENetPeerConnection> s_Connections;

        static ENetPeerConnection()
        {
            s_Counter     = 1;
            s_Connections = new Dictionary<long, ENetPeerConnection>(32);

            Application.quitting += () =>
            {
                foreach (var con in s_Connections) InternalFree(con.Value);
                s_Connections.Clear();
            };
        }
    }
}
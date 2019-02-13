using System;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared.utils;
using Unity.Mathematics;
using UnityEngine;
using Valve.Sockets;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public unsafe struct NetworkCommands
    {
        // foreign
        private uint m_GnsConnectionId;
        // ...
        // local
        private uint m_GnsListenSocketId;
        private IntPtr m_GnsNative;
        // ...
        
        private byte m_IsPeer;

        public bool IsPeer => m_IsPeer == 1;
        
        public static NetworkCommands CreateFromListenSocket(IntPtr native, uint socketId)
        {
            return new NetworkCommands
            {
                m_IsPeer = 0,
                
                m_GnsConnectionId   = 0,
                m_GnsListenSocketId = socketId,
                m_GnsNative         = native
            };
        }
        
        public static NetworkCommands CreateFromConnection(IntPtr native, uint connectionId)
        {
            return new NetworkCommands
            {
                m_IsPeer = 1,
                
                m_GnsConnectionId   = connectionId,
                m_GnsListenSocketId = 0,
                m_GnsNative         = native
            };
        }

        private GnsExecution GetExecution()
        {
            return new GnsExecution(m_GnsNative, m_GnsListenSocketId);
        }

        public NmLkSpan<NetworkingMessage> ReceiveMessageFromConnection(uint connectionId)
        {
            if (connectionId == 0)
                throw new InvalidOperationException();
                
            var execution = GetExecution();
            return execution.ReceiveMessageOnConnection(connectionId);
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
            var execution = GetExecution();

            if (m_IsPeer == 1)
            {
                var result = execution.SendToConnection(m_GnsConnectionId, ptr, (uint) length, delivery.ToGnsSendTypeFlags());
                if (result != Result.OK)
                    Debug.LogError($"Expected {Result.OK} when sending a message but we got {result}.");
                
                return result == Result.OK;
            }

            Debug.LogWarning("The broadcast method don't exist yet for the ListenSocket type.");
            return false;
        }

        public ConnectionStatus ConnectionStatus
        {
            get
            {
                var execution = GetExecution();
                if (m_IsPeer == 0)
                    throw new InvalidOperationException();

                if (!execution.GetConnectionStatus(m_GnsConnectionId, out var status))
                    throw new Exception();

                return status;
            }
        }

        public bool SendDisconnectSignal(int data)
        {
            if (m_IsPeer == 0)
            {
                Debug.LogError("IsPeer=false");
                return false;
            }

            var execution = GetExecution();
            return execution.CloseConnection(m_GnsConnectionId, data);
        }
    }
}
using System;
using package.stormiumteam.shared.utils;
using Unity.Collections;
using UnityEngine;

namespace Valve.Sockets
{
    public struct GnsExecution
    {
        public IntPtr NativePtr;
        public uint TargetListenSocket;

        public GnsExecution(IntPtr nativePtr, uint targetListenSocket)
        {
            NativePtr = nativePtr;
            TargetListenSocket = targetListenSocket;
        }
        
        public void RunConnectionStatusCallback(StatusCallback callback, IntPtr ctx)
        {
            Native.SteamAPI_ISteamNetworkingSockets_RunConnectionStatusChangedCallbacks(NativePtr, callback, ctx);
        }

        public NmLkSpan<NetworkingMessage> ReceiveMessageOnListenSocket()
        {
            IntPtr msgPtr;
            
            var count = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnListenSocket(NativePtr, TargetListenSocket, out msgPtr, byte.MaxValue);
            if (count < 0)
                throw new InvalidOperationException();
            
            return new NmLkSpan<NetworkingMessage>(msgPtr, count);
        }
        
        public NmLkSpan<NetworkingMessage> ReceiveMessageOnConnection(uint connection)
        {
            IntPtr msgPtr;
            
            var count = Native.SteamAPI_ISteamNetworkingSockets_ReceiveMessagesOnConnection(NativePtr, connection, out msgPtr, byte.MaxValue);
            if (count < 0)
                throw new InvalidOperationException($"Invalid connection ({connection}) handle");
            
            return new NmLkSpan<NetworkingMessage>(msgPtr, count);
        }

        public void SetConnectionData(uint connection, long data)
        {
            Native.SteamAPI_ISteamNetworkingSockets_SetConnectionUserData(NativePtr, connection, data);
        }
        
        public long GetConnectionData(uint msgConnection)
        {
            return Native.SteamAPI_ISteamNetworkingSockets_GetConnectionUserData(NativePtr, msgConnection);
        }

        public Result AcceptConnection(uint connection)
        {
            Debug.Log(NativePtr + " against " + connection);
            
            return Native.SteamAPI_ISteamNetworkingSockets_AcceptConnection(NativePtr, connection);
        }

        public bool CloseConnection(uint connection, int reason = 0, string debug = null, bool enableLinger = true)
        {
            if (debug == null)
                debug = string.Empty;
            
            return Native.SteamAPI_ISteamNetworkingSockets_CloseConnection(NativePtr, connection, reason, debug, enableLinger);
        }

        public Result SendToConnection(uint connection, IntPtr data, uint length, SendType flags)
        {
            return Native.SteamAPI_ISteamNetworkingSockets_SendMessageToConnection(NativePtr, connection, data, length, flags);
        }

        public bool GetConnectionStatus(uint connection, out ConnectionStatus connectionStatus)
        {
            connectionStatus = default;
            
            return Native.SteamAPI_ISteamNetworkingSockets_GetQuickConnectionStatus(NativePtr, connection, ref connectionStatus);
        }
    }
}
using System;
using System.Net;
using UnityEngine;
using Valve.Sockets;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public enum NetDriverBindError
    {
        Success           = 0,
        NativeError       = 1,
        HostAlreadyBind = 2,
        HostNull          = 3
    }

    public struct NetDriver
    {
        private bool m_HostWasAlreadyBind;
        
        public NetworkingSockets Sockets;

        public NetDriver(IntPtr ctx)
        {
            Sockets = new NetworkingSockets();

            m_HostWasAlreadyBind = false;
        }

        public NetDriverBindError Listen(Address address, out uint socketId)
        {
            Debug.Log("Previous Address: " + address.GetIP());
            
            //address = new Address();
            //address.SetIPv4("127.0.0.1", 9000);
            
            Debug.Log("Next Address: " + address.GetIP());
            
            if (m_HostWasAlreadyBind)
            {
                socketId = 0;
                return NetDriverBindError.HostAlreadyBind;
            }
            
            socketId = Sockets.CreateListenSocket(address);
            if (socketId == 0)
                return NetDriverBindError.NativeError;

            m_HostWasAlreadyBind = true;
            
            return NetDriverBindError.Success;
        }

        public NetDriverBindError Connect(Address address, out uint serverPeerConnectionId)
        {
            Debug.Log("Previous Address: " + address.GetIP());
            
            //address = new Address();
            //address.SetIPv4("127.0.0.1", 9000);
            
            Debug.Log("Next Address: " + address.GetIP());
            
            if (m_HostWasAlreadyBind)
            {
                serverPeerConnectionId = 0;
                return NetDriverBindError.HostAlreadyBind;
            }

            serverPeerConnectionId = Sockets.Connect(address);
            if (serverPeerConnectionId == 0)
                return NetDriverBindError.NativeError;

            m_HostWasAlreadyBind = true;
            
            return NetDriverBindError.Success;
        }
    }
}
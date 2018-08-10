﻿using System;
using System.Net;
using DefaultNamespace;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.networking;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking
{
    [AlwaysUpdateSystem]
    public class ConnectionEventManager : NetworkConnectionSystem
    {
        public event Action<NetManager> OnBeforeNetStop;
        public event Action<NetManager> OnAfterNetStop;

        public IConnection NetLibConnection;

        [Inject] private ConnectionChannelManager m_ChannelManager;

        protected override void OnCreateManager(int capacity)
        {
            Assert.IsFalse(NetInstance == null, "NetInstance == null");
            Assert.IsFalse(NetInstance.ConnectionInfo.Creator == null, "NetInstance.ConnectionInfo.Creator == null");

            NetLibConnection = NetInstance.ConnectionInfo.Creator as IConnection;
            Enabled          = NetLibConnection != null;

            if (NetLibConnection is IConnectionHost host)
            {
                host.Listener.ConnectionRequestEvent += ListenerOnConnectionRequestEvent;
                ;
                host.Listener.NetworkErrorEvent              += ListenerOnNetworkErrorEvent;
                host.Listener.NetworkLatencyUpdateEvent      += ListenerOnNetworkLatencyUpdateEvent;
                host.Listener.NetworkReceiveEvent            += ListenerOnNetworkReceiveEvent;
                host.Listener.NetworkReceiveUnconnectedEvent += ListenerOnNetworkReceiveUnconnectedEvent;
                host.Listener.PeerConnectedEvent             += ListenerOnPeerConnectedEvent;
                host.Listener.PeerDisconnectedEvent          += ListenerOnPeerDisconnectedEvent;
            }
        }

        private void ListenerOnConnectionRequestEvent(ConnectionRequest request)
        {
            Debug.Log($"[<b>{nameof(ConnectionEventManager)}</b>] We received a new connection request");
            
            foreach (var manager in AppEvent<INetConnectionRequestEvent>.eventList)
                manager.Callback(NetInstance, request);
        }

        private void ListenerOnPeerConnectedEvent(NetPeer peer)
        {
            NetworkInstance GetNetworkInstance()
            {
                foreach (var otherInstance in NetInstance.m_Interconnections)
                {
                    if (otherInstance.ConnectionInfo.ConnectionType == ConnectionType.Out
                        && otherInstance.ConnectionInfo.Creator.GetAddress().Equals(peer.EndPoint))
                        return otherInstance;
                    
                    // Also search in acknowledged instances
                }
                
                var connectionCreator = new NetworkInConnectionCreator();
                connectionCreator.ConnectedPeer  = peer;
                connectionCreator.IpAddress      = peer.EndPoint.Address.ToString();
                connectionCreator.Port           = peer.EndPoint.Port;
                connectionCreator.Manager        = peer.NetManager;
                connectionCreator.ManagerAddress = "127.0.0.1";
                connectionCreator.ManagerPort    = (short) peer.NetManager.LocalPort;

                connectionCreator.Init();

                var networkInstance = new NetworkInstance(new NetworkConnectionInfo()
                {
                    ConnectionType = ConnectionType.In,
                    Creator        = connectionCreator
                }, false, true);

                connectionCreator.Execute(networkInstance);

                networkInstance.SetMain(NetInstance, true);

                MainWorld.GetOrCreateManager<NetworkManager>().AddInstance(networkInstance, ConnectionType.In, NetInstance);

                return networkInstance;
            }
            
            Debug.Log(
                $"[<b>{nameof(ConnectionEventManager)}</b>] <color='green'>{peer.EndPoint}</color> made a connection to our network.");

            var otherNetworkInstance = GetNetworkInstance();
            var newPeerInstance = new NetPeerInstance(otherNetworkInstance, m_ChannelManager.DefaultChannel, peer);
            peer.Tag = newPeerInstance;

            var allocatedUser = NetInstance.GetUserManager().Allocate();
            otherNetworkInstance.SetUser(allocatedUser);

            foreach (var manager in AppEvent<INetPeerConnectedEvent>.eventList)
                manager.Callback(NetInstance, newPeerInstance);

            NetInstance.BroadcastData(newPeerInstance);
            
            otherNetworkInstance.SetAsConnected();

            foreach (var manager in AppEvent<INetSentAllBroadcastedEventToPeer>.eventList)
                manager.Callback(NetInstance, newPeerInstance);
        }

        private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log(
                $"[<b>{nameof(ConnectionEventManager)}</b>] <color='red'>{peer.EndPoint}</color> disconnected ({disconnectInfo.Reason}).");
            
            foreach (var manager in AppEvent<INetPeerDisconnectedEvent>.eventList)
                manager.Callback(NetInstance, peer, disconnectInfo);

            if (peer.Tag == null)
                return;
            
            // Destroy peer world
            var peerInstance = peer.Tag as NetPeerInstance;
            
            Assert.IsTrue(peerInstance != null, "peerInstance != null");
            
            NetInstance.GetUserManager().Dispose(peerInstance.NetUser);
            peerInstance.Dispose();
            
            peer.Tag = null;
            
            Debug.Log($"[<b>{nameof(ConnectionEventManager)}</b>] Disposed peer instance (<color='red'>{peer.EndPoint}</color>)");
        }

        private void ListenerOnNetworkErrorEvent(IPEndPoint endPoint, int socketErrorCode)
        {
            Debug.Log($"[<b>{nameof(ConnectionEventManager)}</b>] Network Error: {socketErrorCode} from {endPoint}");
            
            foreach (var manager in AppEvent<INetErrorEvent>.eventList)
                manager.Callback(NetInstance, endPoint, socketErrorCode);
        }

        private void ListenerOnNetworkReceiveUnconnectedEvent(IPEndPoint             remoteEndPoint,
                                                              NetDataReader          reader,
                                                              UnconnectedMessageType messageType)
        {
            foreach (var manager in AppEvent<INetReceiveReceiveUnconnectedEvent>.eventList)
                manager.Callback(NetInstance, remoteEndPoint, reader, messageType);
        }

        private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
        {
            var messageType = reader.GetByte();
            var msgReader = new MessageReader
            {
                Data    = reader,
                IntType = messageType
            };

            MainWorld.GetOrCreateManager<NetworkMessageSystem>()
                     .TriggerOnNewMessage(peer.Tag as NetPeerInstance, msgReader);
        }

        private void ListenerOnNetworkLatencyUpdateEvent(NetPeer peer, int latency)
        {
            //NetworkLatencyUpdateEvent?.Invoke(peer, latency);
        }

        protected override void OnUpdate()
        {
            NetLibConnection.Manager?.PollEvents();
        }

        protected override void OnDestroyManager()
        {
            if (NetLibConnection?.Manager == null)
                return;
            if (NetInstance.ConnectionInfo.ConnectionType != ConnectionType.Self)
                return;

            var manager = NetLibConnection.Manager;

            Profiler.BeginSample("Stop manager");
            OnBeforeNetStop?.Invoke(manager);
            manager?.Stop();
            OnAfterNetStop?.Invoke(manager);
            
            // Dispose other connections that is linked to this one
            var networkManager = MainWorld.GetOrCreateManager<NetworkManager>();
            foreach (var inter in NetInstance.m_Interconnections)
            {
                var continueLoop = false;
                
                foreach (var otherSelf in networkManager.Self)
                {
                    if (NetInstance == otherSelf)
                        continue;
                    
                    foreach (var otherInter in otherSelf.Interconnections)
                    {
                        if (inter == otherInter)
                            continueLoop = true;
                    }
                }

                if (continueLoop) continue;
                
                inter.Dispose();
            }
            
            Profiler.EndSample();
            
            Debug.Log($"{nameof(ConnectionEventManager)} has stopped the manager connection");
        }
    }
}
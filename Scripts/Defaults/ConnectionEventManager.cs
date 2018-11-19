using System;
using System.Net;
using package.stormiumteam.networking.plugins;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.networking;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.PlayerLoop;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking
{
    [AlwaysUpdateSystem, UpdateBefore(typeof(EarlyUpdate))]
    public class ConnectionEventManager : NetworkConnectionSystem
    {
        public event Action<NetManager> OnBeforeNetStop;
        public event Action<NetManager> OnAfterNetStop;

        public IConnection NetLibConnection;

        [Inject] private ConnectionChannelManager m_ChannelManager;

        protected override void OnCreateManager()
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

            foreach (var manager in AppEvent<EventConnectionRequest.IEv>.eventList)
                manager.Callback(new EventConnectionRequest.Arguments(NetInstance, request));
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

                Profiler.BeginSample("New Connection Creator");
                var connectionCreator = new NetworkInConnectionCreator();
                connectionCreator.ConnectedPeer  = peer;
                connectionCreator.IpAddress      = peer.EndPoint.Address.ToString();
                connectionCreator.Port           = peer.EndPoint.Port;
                connectionCreator.Manager        = peer.NetManager;
                connectionCreator.ManagerAddress = "127.0.0.1";
                connectionCreator.ManagerPort    = (short) peer.NetManager.LocalPort;
                Profiler.EndSample();
                Profiler.BeginSample("Init()");
                connectionCreator.Init();
                Profiler.EndSample();

                Profiler.BeginSample("new NetworkInstance()");
                var networkInstance = new NetworkInstance(new NetworkConnectionInfo()
                {
                    ConnectionType = ConnectionType.In,
                    Creator        = connectionCreator
                }, false, true);
                Profiler.EndSample();

                Profiler.BeginSample("Execute()");
                connectionCreator.Execute(networkInstance);
                Profiler.EndSample();
                Profiler.BeginSample("SetMain()");
                networkInstance.SetMain(NetInstance, true);
                Profiler.EndSample();
                Profiler.BeginSample("AddInstance");
                MainWorld.GetOrCreateManager<NetworkManager>().AddInstance(networkInstance, ConnectionType.In, NetInstance);
                Profiler.EndSample();
                return networkInstance;
            }

            Debug.Log(
                $"[<b>{nameof(ConnectionEventManager)}</b>] <color='green'>{peer.EndPoint}</color> made a connection to our network.");

            Profiler.BeginSample("GetNetworkInstance()");
            var otherNetworkInstance = GetNetworkInstance();
            Profiler.EndSample();
            Profiler.BeginSample("new NetPeerInstance()");
            var newPeerInstance      = new NetPeerInstance(otherNetworkInstance, m_ChannelManager.DefaultChannel, peer);
            peer.Tag = newPeerInstance;
            otherNetworkInstance.SetPeerInstance(newPeerInstance);
            Profiler.EndSample();

            Profiler.BeginSample("Allocate User");
            var allocatedUser = NetInstance.GetUserManager().Allocate(newPeerInstance);
            otherNetworkInstance.SetUser(allocatedUser);
            Profiler.EndSample();
            
            Profiler.BeginSample("Call Events");
            foreach (var manager in AppEvent<EventPeerConnected.IEv>.eventList)
                manager.Callback(new EventPeerConnected.Arguments(NetInstance, newPeerInstance));
            Profiler.EndSample();
            
            Profiler.BeginSample("Broadcast Data");
            NetInstance.BroadcastData(newPeerInstance);
            Profiler.EndSample();
            
            Profiler.BeginSample("Set as connected");
            otherNetworkInstance.SetAsConnected();
            Profiler.EndSample();
                
            Profiler.BeginSample("Call Events (2)");
            foreach (var manager in AppEvent<EventPeerReceiveBroadcastData.IEv>.eventList)
                manager.Callback(new EventPeerReceiveBroadcastData.Arguments(NetInstance, newPeerInstance));
            Profiler.EndSample();
        }

        private void ListenerOnPeerDisconnectedEvent(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            Debug.Log(
                $"[<b>{nameof(ConnectionEventManager)}</b>] <color='red'>{peer.EndPoint}</color> disconnected ({disconnectInfo.Reason}).");

            foreach (var manager in AppEvent<EventPeerDisconnected.IEv>.eventList)
                manager.Callback(new EventPeerDisconnected.Arguments(NetInstance, peer, disconnectInfo));

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

            foreach (var manager in AppEvent<EventNetworkError.IEv>.eventList)
                manager.Callback(new EventNetworkError.Arguments(NetInstance, endPoint, socketErrorCode));
        }

        private void ListenerOnNetworkReceiveUnconnectedEvent(IPEndPoint             remoteEndPoint,
                                                              NetDataReader          reader,
                                                              UnconnectedMessageType messageType)
        {
            foreach (var manager in AppEvent<EventReceiveUnconnectedData.IEv>.eventList)
                manager.Callback(new EventReceiveUnconnectedData.Arguments(NetInstance, remoteEndPoint, reader, messageType));
        }

        private void ListenerOnNetworkReceiveEvent(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
        {
            if (reader.Data.Length <= 0)
            {
                Debug.LogError("Invalid data from a peer!");
                // peer.Disconnect();
            }
            
            var messageType = reader.GetByte();
            var msgReader = new MessageReader
            {
                Data    = reader,
                IntType = messageType
            };

            MainWorld.GetOrCreateManager<NetworkMessageSystem>()
                     .TriggerOnNewMessage(NetInstance, peer.Tag as NetPeerInstance, msgReader);
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
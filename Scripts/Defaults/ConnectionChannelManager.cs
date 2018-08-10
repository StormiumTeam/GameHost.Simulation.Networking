using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.networking;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public enum ChannelManagement
    {
        /// <summary>
        /// All channels will be simulated from the main one
        /// </summary>
        MergeAllInMain,
        /// <summary>
        /// All channels will be created from a port list
        /// </summary>
        CreateFromList,
        /// <summary>
        /// All channels will be created from a port list, if there is no free ports, new channels will be merged in main
        /// </summary>
        CreateFromListOrMergeInMain,
        /// <summary>
        /// All channels will be created from a port list, if there is no free ports, it will select a random one
        /// </summary>
        CreateFromListOrCreateFreely,
        /// <summary>
        /// All channel will be created freely with a random free port
        /// </summary>
        CreateFreely
    }
    
    public class ConnectionChannelManager : NetworkConnectionSystem
    {        
        private List<NetworkChannel> m_AllChannels;
        private NetworkChannel       m_DefaultChannel;

        public ReadOnlyCollection<NetworkChannel> Channels;
        public NetworkChannel                     DefaultChannel => m_DefaultChannel;

        private List<NetDataWriter> m_MessagesWriteAddChannel;

        private ChannelManagement m_Option;
        public List<int> UsablesPorts = new List<int>();

        protected override void OnCreateManager(int capacity)
        {
            m_AllChannels = new List<NetworkChannel>();
            Channels      = new ReadOnlyCollection<NetworkChannel>(m_AllChannels);
            m_MessagesWriteAddChannel = new List<NetDataWriter>();
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            BroadcastChannelManagement(peerInstance.Peer);
            
            // If we are not a host, we don't send any channel data
            // Or if the peer channel isn't the default one
            if (!NetInstance.SelfHost
                || peerInstance.Channel != m_DefaultChannel)
                return;

            Debug.Log( 
                $"[<b>{nameof(ConnectionChannelManager)}</b>] Broadcasting data to {peerInstance.Global.World.Name}");

            var peer = peerInstance.Peer;

            // Broadcast default channel
            foreach (var msg in m_MessagesWriteAddChannel)
            {
                peer.Send(msg, DeliveryMethod.ReliableOrdered);
            }
        }

        public override void OnInstanceGettingReady()
        {
            Debug.Log($"[<b>{nameof(ConnectionChannelManager)}</b>] New operation: Set the default channel");

            var connection     = NetInstance.ConnectionInfo.Creator as IConnection;
            var hostConnection = NetInstance.ConnectionInfo.Creator as IConnectionHost;
            Assert.IsFalse(connection == null, "connection == null");

            var data    = new NetworkChannelData(NetInstance, NetConstants.Channel_Main, -1, null, DeliveryMethod.ReliableOrdered);
            var channel = new NetworkChannel(data, connection.Manager, hostConnection?.Listener);

            SetDefaultChannel(channel);
        }

        private ChannelManagement m_LastOption;

        protected override void OnUpdate()
        {
        }

        protected override void OnDestroyManager()
        {
            m_AllChannels.Clear();
            m_MessagesWriteAddChannel.Clear();

            m_AllChannels    = null;
            Channels         = null;
            m_DefaultChannel = null;
            m_MessagesWriteAddChannel = null;
        }

        internal void BroadcastChannelManagement(NetPeer peer)
        {
            var msg = new NetDataWriter(true, sizeof(byte)
                                              + sizeof(int) * 2);
            msg.Put((byte) MessageType.Internal);
            msg.Put((int) InternalMessageType.SetChannelOption);
            msg.Put((int) m_Option);

            peer.Send(msg, DeliveryMethod.ReliableOrdered);
        }

        internal void SetDefaultChannel(NetworkChannel channel)
        {
            if (m_AllChannels.Contains(channel))
            {
                m_AllChannels.Remove(channel);
            }

            m_AllChannels.Insert(0, channel);
            m_DefaultChannel = channel;
        }

        public NetworkChannel DeployChannelToAll(NetworkChannelIdent ident, 
                                                 NetManagerConfiguration? configuration,
                                                 DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,
                                                 bool                startListening = true)
        {
            return DeployChannelToAll(ident.Id, configuration, deliveryMethod, ident.Port, startListening);
        }

        public NetworkChannel DeployChannelToAll(string id, 
                                                 NetManagerConfiguration? configuration,
                                                 DeliveryMethod deliveryMethod = DeliveryMethod.ReliableOrdered,
                                                 int requestedPort = 0,
                                                 bool   startListening = true)
        {
            if (NetInstance.ConnectionInfo.ConnectionType != ConnectionType.Self)
                throw new Exception();

            var channelData = new NetworkChannelData(NetInstance, id, requestedPort, configuration, deliveryMethod);
            var channel     = new NetworkChannel(channelData);

            if (startListening) channel.StartListening();

            var netDataWriter = new NetDataWriter();
            netDataWriter.Put((byte)MessageType.Internal);
            netDataWriter.Put((int)InternalMessageType.DeployChannel);
            netDataWriter.PutChannelId(new NetworkChannelIdent(channel.Data.Id, channel.Data.Port));
            
            m_AllChannels.Add(channel);
            m_MessagesWriteAddChannel.Add(netDataWriter);

            Debug.Log($"Broadcasting a new channel <b>({channel.Data.Id}):{channel.Manager.LocalPort}</b>");

            return channel;
        }

        public bool Has(NetworkChannelIdent channelIdent)
        {
            if (m_DefaultChannel != null 
                && (m_DefaultChannel.Data.Id == channelIdent.Id || m_Option == ChannelManagement.MergeAllInMain))
                return true;

            foreach (var value in m_AllChannels)
                if (value.Data.Id == channelIdent.Id)
                    return true;

            return false;
        }

        public NetworkChannel Get(NetworkChannelIdent channelIdent)
        {
            if (m_DefaultChannel != null 
                && (m_DefaultChannel.Data.Id == channelIdent.Id || m_Option == ChannelManagement.MergeAllInMain))
                return m_DefaultChannel;

            foreach (var value in m_AllChannels)
                if (value.Data.Id == channelIdent.Id)
                    return value;

            return null;
        }

        public void ChangeManagement(ChannelManagement newOption)
        {
            m_Option = newOption;

            var manager = DefaultChannel.Manager;

            foreach (var peer in manager)
                BroadcastChannelManagement(peer);
        }

        public bool Is(NetworkChannel networkChannel, string intern)
        {
            if (m_Option == ChannelManagement.MergeAllInMain)
                return true;
            
            var connection = NetInstance.ConnectionInfo.Creator as IConnection;
            
            Assert.IsTrue(connection != null, nameof(connection) + " != null");
            
            if (networkChannel.Manager.LocalPort == connection.Manager.LocalPort)
                return true;
            
            var chanId = networkChannel.Data.Id;
            return chanId == intern;
        }
    }
}
using System;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public class NetPeerInstance : IDisposable
    {
        private static NetDataWriter s_ServerAllBroadcastedDataSent;
        private static NetDataWriter s_ClientAllBroadcastedDataReceived;

        public NetworkInstance Global      { get; private set; }
        public NetworkChannel  Channel     { get; private set; }
        public NetPeer         Peer        { get; private set; }
        public bool            ServerReady { get; private set; }
        public bool            ClientReady { get; private set; }

        public NetUser NetUser => Global.NetUser;
        public ConnectionType ConnectionType => Global.ConnectionInfo.ConnectionType;

        static NetPeerInstance()
        {
            s_ServerAllBroadcastedDataSent = new NetDataWriter();
            s_ServerAllBroadcastedDataSent.Put((byte) MessageType.Internal);
            s_ServerAllBroadcastedDataSent.Put((int) InternalMessageType.AllBroadcastedDataSent);

            s_ClientAllBroadcastedDataReceived = new NetDataWriter();
            s_ClientAllBroadcastedDataReceived.Put((byte) MessageType.Internal);
            s_ClientAllBroadcastedDataReceived.Put((int) InternalMessageType.AllBroadcastedDataReceived);
        }

        public NetPeerInstance(NetworkInstance networkInstance, NetworkChannel channel, NetPeer peer)
        {
            Global  = networkInstance;
            Channel = channel;
            Peer    = peer;

            ServerReady = false;
            ClientReady = false;
        }

        public void Dispose()
        {
            Global.Dispose();

            Global  = null;
            Channel = null;
            Peer    = null;
        }

        public void SetInitialized()
        {
            ServerReady = true;
        }

        public void SetClientReady()
        {
            ClientReady = true;
        }

        internal void AllBroadcastedDataSent()
        {
            Assert.IsTrue(ServerReady, "NetPeerInstance.ServerReady");
            
            Peer.Send(s_ServerAllBroadcastedDataSent, DeliveryMethod.ReliableOrdered);
        }

        internal void AllBroadcastedDataReceived()
        {
            Assert.IsTrue(ClientReady, "NetPeerInstance.ClientReady");
            
            Peer.Send(s_ClientAllBroadcastedDataReceived, DeliveryMethod.ReliableOrdered);
        }
    }
}
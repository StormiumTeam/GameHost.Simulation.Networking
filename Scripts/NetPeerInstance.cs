using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public class NetPeerInstance : IDisposable
    {
        private static NetDataWriter s_ServerAllBroadcastedDataSent;
        private static NetDataWriter s_ClientAllBroadcastedDataReceived;

        private static int                   s_IncrementId               = 0;
        private static List<NetPeerInstance> s_AllCreationValidInstances = new List<NetPeerInstance>();

        public int             Id          { get; private set; }
        public NetworkInstance Global      { get; private set; }
        public NetworkChannel  Channel     { get; private set; }
        public NetPeer         Peer        { get; private set; }
        public bool            ServerReady { get; private set; }
        public bool            ClientReady { get; private set; }

        public NetUser        NetUser        => Global.NetUser;
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
            Id = s_IncrementId;

            Global  = networkInstance;
            Channel = channel;
            Peer    = peer;

            ServerReady = false;
            ClientReady = false;

            s_IncrementId++;
            s_AllCreationValidInstances.Add(this);
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

            var managers = AppEvent<EventPeerInstanceServerReady.IEv>.GetObjEvents();
            foreach (var manager in managers)
            {
                AppEvent<EventPeerInstanceServerReady.IEv>.Caller = this;
                manager.Callback(new EventPeerInstanceServerReady.Arguments(this));
            }
        }

        public void SetClientReady()
        {
            ClientReady = true;
            
            var managers = AppEvent<EventPeerInstanceClientReady.IEv>.GetObjEvents();
            foreach (var manager in managers)
            {
                AppEvent<EventPeerInstanceClientReady.IEv>.Caller = this;
                manager.Callback(new EventPeerInstanceClientReady.Arguments(this));
            }
        }
        
        public TSystem Get<TSystem>() where TSystem : ComponentSystem
        {
            return Global.World.GetOrCreateManager<TSystem>();
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

        public static explicit operator NetworkInstance(NetPeerInstance peerInstance)
        {
            return peerInstance.Global;
        }

        public static explicit operator NetPeer(NetPeerInstance peerInstance)
        {
            return peerInstance.Peer;
        }

        public static NetPeerInstance FromId(int id)
        {
            if (s_AllCreationValidInstances.Count <= id)
                return null;

            return s_AllCreationValidInstances[id];
        }
    }
    
    
}
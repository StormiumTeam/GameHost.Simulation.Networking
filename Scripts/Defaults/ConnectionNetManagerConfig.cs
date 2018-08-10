using System;
using LiteNetLib;
using LiteNetLib.Utils;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class ConnectionNetManagerConfig : NetworkConnectionSystem
    {
        public NetManager NetManager;

        private int m_LastUpdateTime;

        public int ConfigUpdateTime;

        public override void OnInstanceGettingReady()
        {
            if (NetInstance.ConnectionInfo.ConnectionType == ConnectionType.Self)
                NetManager = (NetInstance.ConnectionInfo.Creator as IConnection)?.Manager;

            NetworkMessageSystem.OnNewMessage += OnNewMessage;
        }

        private void OnNewMessage(NetworkInstance caller, NetPeerInstance netPeerInstance, MessageReader reader)
        {
            reader.ResetReadPosition();

            if (reader.Type != MessageType.Internal)
                return;

            var intType = (InternalMessageType) reader.Data.GetInt();
            if (intType == InternalMessageType.SendNetManagerConfig)
            {
                ConfigUpdateTime = reader.Data.GetInt();
            }
        }

        protected override void OnUpdate()
        {
            if (NetManager == null)
                return;

            var update = !(m_LastUpdateTime == NetManager.UpdateTime);
            if (update)
            {
                m_LastUpdateTime = NetManager.UpdateTime;
                ConfigUpdateTime = m_LastUpdateTime;

                var writer = new NetDataWriter();
                writer.Put((byte) MessageType.Internal);
                writer.Put((int) InternalMessageType.SendNetManagerConfig);
                writer.Put(m_LastUpdateTime);
                
                NetManager.SendToAll(writer, DeliveryMethod.ReliableOrdered);
            }
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            base.OnInstanceBroadcastingData(peerInstance);
            
            var writer = new NetDataWriter();
            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.SendNetManagerConfig);
            writer.Put(m_LastUpdateTime);
                
            peerInstance.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}
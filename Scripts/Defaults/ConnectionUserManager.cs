using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using package.stormiumteam.networking.plugins;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.networking.Classes;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class ConnectionUserManager : NetworkConnectionSystem
    {
        private NetworkUsers                     m_Users;
        private Dictionary<ulong, NetDataWriter> m_AllUserWriters = new Dictionary<ulong, NetDataWriter>();

        [Inject] private NetworkMessageSystem m_MessageSystem;

        protected override void OnCreateManager()
        {
            m_Users = new NetworkUsers(NetInstance);
            
            NetInstance.AllInUsers = new ReadOnlyCollection<NetUser>(m_Users.AllUsers);

            NetworkMessageSystem.OnNewMessage += OnNewMessage;
        }

        protected override void OnUpdate()
        {
        }

        public bool Contains(NetUser user)
        {
            return EntityManager.Exists(GetEntity(user));
        }

        public NetUser Allocate(NetPeerInstance peerInstance)
        {
            var user   = m_Users.Allocate(peerInstance);
            var writer = new NetDataWriter();

            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.AddUser);
            PutUserId(writer, user);

            m_AllUserWriters[user.Index] = writer;

            return user;
        }

        public void Dispose(NetUser user)
        {
            if (m_Users.Dispose(user))
            {
                if (m_AllUserWriters.ContainsKey(user.Index)) m_AllUserWriters.Remove(user.Index);
            }

            // Tell that  to other peers
            var mainChannel = NetInstance.GetChannelManager().DefaultChannel;
            var writer      = new NetDataWriter();
            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.RemUser);
            PutUserId(writer, user);

            m_MessageSystem.InstantSendToAll(mainChannel, writer, DeliveryMethod.ReliableOrdered);
        }

        public ConnectionType GetUserConnectionType(NetUser user)
        {
            return m_Users.GetUserConnectionType(user);
        }

        public Entity GetEntity(NetUser user)
        {
            return m_Users.GetEntity(user);
        }


        public void PutUserId(NetDataWriter dataWriter, NetUser user)
        {
            dataWriter.Put(user.Index);
        }

        public NetUser GetUserId(NetDataReader dataReader)
        {
            var ident = new NetUser(NetInstance.PeerInstance, NetInstance, dataReader.GetULong());
            return ident;
        }

        public NetUser GetUserId(MessageReader reader)
        {
            return GetUserId(reader.Data);
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            if (!peerInstance.Channel.IsMain())
                return;

            var peer = peerInstance.Peer;
            foreach (var dataWriter in m_AllUserWriters.Values)
            {
                m_MessageSystem.InstantSendTo(peer, null, dataWriter, DeliveryMethod.ReliableOrdered);
            }

            var writer = new NetDataWriter();
            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.SetUser);
            PutUserId(writer, peerInstance.NetUser);

            m_MessageSystem.InstantSendTo(peer, null, writer, DeliveryMethod.ReliableOrdered);
        }

        private void OnNewMessage(NetworkInstance caller, NetPeerInstance peerInstance, MessageReader messageReader)
        {
            if (caller != NetInstance) return;

            messageReader.ResetReadPosition();

            var peekMsgType = (InternalMessageType) messageReader.Data.PeekInt();
            if (messageReader.Type != MessageType.Internal
                && peekMsgType != InternalMessageType.AddUser
                && peekMsgType != InternalMessageType.RemUser
                && peekMsgType != InternalMessageType.SetUser)
                return;

            messageReader.Data.GetInt(); //< skip as we already peeked into it

            if (peekMsgType == InternalMessageType.AddUser)
            {
                var user = GetUserId(messageReader);
                m_Users.Add(user);

                var properties = m_Users.GetProperties(user);
                if (!properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>().TriggerOnUserEvent(peerInstance, user, StatusChange.Added);

                    properties.WasAdded = true;
                    m_Users.SetProperties(user, properties);
                }
            }

            if (peekMsgType == InternalMessageType.RemUser)
            {
                var user  = GetUserId(messageReader);
                var index = m_Users.IndexOf(user);
                if (index != -1)
                {
                    m_Users.RemoveAt(index);
                }

                var properties = m_Users.GetProperties(user);
                if (properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>().TriggerOnUserEvent(peerInstance, user, StatusChange.Removed);

                    properties.WasAdded = false;
                    m_Users.SetProperties(user, properties);
                }
            }

            if (peekMsgType == InternalMessageType.SetUser
                && peerInstance.ConnectionType == ConnectionType.Out)
            {
                var user = GetUserId(messageReader);
                if (!m_Users.Contains(user))
                {
                    m_Users.Add(user);
                }

                var properties = m_Users.GetProperties(user);
                if (!properties.WasAdded && !properties.WasMainUser)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.NewUserAsMain);

                    properties.WasAdded    = true;
                    properties.WasMainUser = true;
                    m_Users.SetProperties(user, properties);
                }
                else if (!properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.Added);

                    properties.WasAdded = true;
                    m_Users.SetProperties(user, properties);
                }
                else if (!properties.WasMainUser)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.MainUser);

                    properties.WasMainUser = true;
                    m_Users.SetProperties(user, properties);
                }
                else
                {
                    Debug.Log("empty event");
                }

                NetInstance.SetUser(user);

                Debug.Log($"We got our own user ({user.Index}) from {peerInstance.Global.World.Name}");
            }
        }
    }
}
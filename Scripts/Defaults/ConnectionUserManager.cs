using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DefaultNamespace;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class ConnectionUserManager : NetworkConnectionSystem
    {
        struct NetUserProperties
        {
            public bool WasAdded;
            public bool WasMainUser;
        }

        private World                                m_UserWorld;
        private EntityManager                        m_UserEntityManager;
        private List<NetUser>                        m_AllUsers       = new List<NetUser>();
        private List<NetDataWriter>                  m_AllUserWriters = new List<NetDataWriter>();
        private Dictionary<ulong, NetUserProperties> m_UserProperties = new Dictionary<ulong, NetUserProperties>();

        [Inject] private NetworkMessageSystem m_MessageSystem;

        protected override void OnCreateManager(int capacity)
        {
            m_UserWorld         = new World("__netuser__" + NetWorld.Name);
            m_UserEntityManager = m_UserWorld.GetOrCreateManager<EntityManager>();

            NetInstance.AllInUsers = new ReadOnlyCollection<NetUser>(m_AllUsers);

            NetworkMessageSystem.OnNewMessage += OnNewMessage;
        }

        protected override void OnUpdate()
        {
        }

        protected override void OnDestroyManager()
        {
            m_UserWorld.Dispose();
        }

        private NetUserProperties GetProperties(NetUser user)
        {
            NetUserProperties properties;
            if (!m_UserProperties.TryGetValue(user.Index, out properties))
                properties = m_UserProperties[user.Index] = new NetUserProperties();
            return properties;
        }

        private void SetProperties(NetUser user, NetUserProperties properties)
        {
            m_UserProperties[user.Index] = properties;
        }

        public bool Contains(NetUser user)
        {
            return m_UserEntityManager.Exists(GetEntity(user));
        }

        public NetUser Allocate()
        {
            var entity = m_UserEntityManager.CreateEntity();

            var user = new NetUser(NetInstance, StMath.DoubleIntToULong(entity.Index, entity.Version));

            m_AllUsers.Add(user);

            var writer = new NetDataWriter();
            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.AddUser);
            PutUserId(writer, user);

            m_AllUserWriters.Add(writer);

            var properties = GetProperties(user);
            if (!properties.WasAdded)
            {
                MainWorld.GetExistingManager<NetworkUserSystem>()
                         .TriggerOnUserEvent(NetInstance.PeerInstance, user, StatusChange.Added);

                properties.WasAdded = true;
                SetProperties(user, properties);
            }

            return user;
        }

        public void Dispose(NetUser user)
        {
            var entity = GetEntity(user);
            if (m_UserEntityManager.Exists(entity)) m_UserEntityManager.DestroyEntity(entity);
            if (m_AllUsers.Contains(user))
            {
                var index = m_AllUsers.IndexOf(user);
                if (index == -1)
                    return;

                m_AllUsers.RemoveAt(index);
                m_AllUserWriters.RemoveAt(index);
            }

            // Tell that  to other peers
            var mainChannel = NetInstance.GetChannelManager().DefaultChannel;
            var writer      = new NetDataWriter();
            writer.Put((byte) MessageType.Internal);
            writer.Put((int) InternalMessageType.RemUser);
            PutUserId(writer, user);

            m_MessageSystem.InstantSendToAll(mainChannel, writer, DeliveryMethod.ReliableOrdered);

            var properties = GetProperties(user);
            if (properties.WasAdded)
            {
                MainWorld.GetExistingManager<NetworkUserSystem>()
                         .TriggerOnUserEvent(NetInstance.PeerInstance, user, StatusChange.Removed);

                properties.WasAdded = false;
                SetProperties(user, properties);
            }
        }

        public ConnectionType GetUserConnectionType(NetUser user)
        {
            var entity = GetEntity(user);
            if (m_UserEntityManager.Exists(entity)
                && m_UserEntityManager.HasComponent<UserRelativeConnectionData>(entity))
            {
                return m_UserEntityManager.GetComponentData<UserRelativeConnectionData>(entity)
                                          .ConnectionType;
            }

            return ConnectionType.Unknow;
        }

        internal Entity GetEntity(NetUser user)
        {
            var intTuple = StMath.ULongToDoubleUInt(user.Index);
            var eIndex   = StMath.UIntToInt(intTuple.Item1);
            var eVersion = StMath.UIntToInt(intTuple.Item2);

            return new Entity
            {
                Index   = eIndex,
                Version = eVersion
            };
        }


        public void PutUserId(NetDataWriter dataWriter, NetUser user)
        {
            dataWriter.Put(user.Index);
        }

        public NetUser GetUserId(NetDataReader dataReader)
        {
            var ident = new NetUser(NetInstance, dataReader.GetULong());
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
            foreach (var dataWriter in m_AllUserWriters)
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
                m_AllUsers.Add(user);

                var properties = GetProperties(user);
                if (!properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.Added);

                    properties.WasAdded = true;
                    SetProperties(user, properties);
                }
            }

            if (peekMsgType == InternalMessageType.RemUser)
            {
                var user  = GetUserId(messageReader);
                var index = m_AllUsers.IndexOf(user);
                if (index != -1)
                {
                    m_AllUsers.RemoveAt(index);
                }

                var properties = GetProperties(user);
                if (properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.Removed);

                    properties.WasAdded = false;
                    SetProperties(user, properties);
                }
            }

            if (peekMsgType == InternalMessageType.SetUser
                && peerInstance.ConnectionType == ConnectionType.Out)
            {
                var user = GetUserId(messageReader);
                if (!m_AllUsers.Contains(user))
                {
                    m_AllUsers.Add(user);
                }

                var properties = GetProperties(user);
                if (!properties.WasAdded && !properties.WasMainUser)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.NewUserAsMain);

                    properties.WasAdded    = true;
                    properties.WasMainUser = true;
                    SetProperties(user, properties);
                }
                else if (!properties.WasAdded)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.Added);

                    properties.WasAdded = true;
                    SetProperties(user, properties);
                }
                else if (!properties.WasMainUser)
                {
                    MainWorld.GetExistingManager<NetworkUserSystem>()
                             .TriggerOnUserEvent(peerInstance, user, StatusChange.MainUser);

                    properties.WasMainUser = true;
                    SetProperties(user, properties);
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
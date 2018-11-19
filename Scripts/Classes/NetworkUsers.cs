using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.networking.plugins;
using package.stormiumteam.shared;
using Unity.Entities;

namespace package.stormiumteam.networking.Classes
{
    public struct NetUserProperties
    {
        public bool WasAdded;
        public bool WasMainUser;
    }

    public struct NetUserTag : IComponentData
    {

    }

    public class NetworkUsers
    {
        internal NetworkInstance NetInstance;

        private EntityManager m_EntityManager => NetInstance.World.GetExistingManager<EntityManager>();

        public List<NetUser>                        AllUsers       = new List<NetUser>();
        public Dictionary<ulong, NetUserProperties> UserProperties = new Dictionary<ulong, NetUserProperties>();

        public NetworkUsers(NetworkInstance netInstance)
        {
            NetInstance = netInstance;
        }

        public NetUserProperties GetProperties(NetUser user)
        {
            NetUserProperties properties;
            if (!UserProperties.TryGetValue(user.Index, out properties))
                properties = UserProperties[user.Index] = new NetUserProperties();
            return properties;
        }

        public void SetProperties(NetUser user, NetUserProperties properties)
        {
            UserProperties[user.Index] = properties;
        }

        public bool Contains(NetUser user)  => AllUsers.Contains(user);
        public int  IndexOf(NetUser  user)  => AllUsers.IndexOf(user);
        public void Add(NetUser      user)  => AllUsers.Add(user);
        public void Remove(NetUser   user)  => AllUsers.Remove(user);
        public void RemoveAt(int     index) => AllUsers.RemoveAt(index);

        public NetUser Allocate(NetPeerInstance peerInstance)
        {
            peerInstance = peerInstance ?? NetInstance.PeerInstance;

            var entity = m_EntityManager.CreateEntity(typeof(NetUserTag));
            var user   = new NetUser(peerInstance, NetInstance, StMath.DoubleIntToULong(entity.Index, entity.Version));

            AllUsers.Add(user);

            var properties = GetProperties(user);
            if (!properties.WasAdded)
            {
                World.Active.GetExistingManager<NetworkUserSystem>().TriggerOnUserEvent(NetInstance.PeerInstance, user, StatusChange.Added);

                properties.WasAdded = true;
                SetProperties(user, properties);
            }

            return user;
        }

        /// <summary>
        /// Dispose a user
        /// </summary>
        /// <param name="user"></param>
        /// <returns>Return true if the user was removed the list 'AllUsers'</returns>
        public bool Dispose(NetUser user)
        {
            var wasRemoved = false;
            var entity     = GetEntity(user);
            if (m_EntityManager.Exists(entity)) m_EntityManager.DestroyEntity(entity);
            if (AllUsers.Contains(user))
            {
                var index = AllUsers.IndexOf(user);
                if (index == -1)
                    return false;

                AllUsers.RemoveAt(index);
                wasRemoved = true;
            }

            var properties = GetProperties(user);
            if (properties.WasAdded)
            {
                World.Active.GetExistingManager<NetworkUserSystem>().TriggerOnUserEvent(NetInstance.PeerInstance, user, StatusChange.Removed);

                properties.WasAdded = false;
                SetProperties(user, properties);
            }

            return wasRemoved;
        }

        public ConnectionType GetUserConnectionType(NetUser user)
        {
            var entity = GetEntity(user);
            if (m_EntityManager.Exists(entity) && m_EntityManager.HasComponent<UserRelativeConnectionData>(entity))
            {
                return m_EntityManager.GetComponentData<UserRelativeConnectionData>(entity).ConnectionType;
            }

            return ConnectionType.Unknow;
        }

        public Entity GetEntity(NetUser user)
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
    }
}
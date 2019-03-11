using System;
using package.stormiumteam.shared;
using Unity.Entities;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public abstract class NetEventInstanceValid
    {
        public struct Arguments : IDelayComponentArguments
        {
            public World  World;
            public int    InstanceId;
            public Entity InstanceEntity;

            public Arguments(World world, int instanceId, Entity instanceEntity)
            {
                World          = world;
                InstanceId     = instanceId;
                InstanceEntity = instanceEntity;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }
    
    /// <summary>
    /// Called when a client was created from the networking library.
    /// For games, it's recommended to create your own event after the client is OK.
    /// </summary>
    public abstract class NetEventClientCreated
    {
        public struct Arguments : IDelayComponentArguments
        {
            public World  World;
            public int    FromInstanceId;
            public Entity FromInstanceEntity;
            public int    ClientInstanceId;
            public Entity ClientInstanceEntity;
            public Entity ClientEntity;

            public Arguments(World  world, int fromInstanceId, Entity fromInstanceEntity, int clientInstanceId, Entity clientInstanceEntity,
                             Entity clientEntity)
            {
                if (world == null) throw new ArgumentNullException(nameof(world));

                World                = world;
                FromInstanceId       = fromInstanceId;
                FromInstanceEntity   = fromInstanceEntity;
                ClientInstanceId     = clientInstanceId;
                ClientInstanceEntity = clientInstanceEntity;
                ClientEntity         = clientEntity;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }
    
    /*public abstract class EventInstanceReady
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public ConnectionType  ConnectionType;

            public Arguments(NetworkInstance caller, ConnectionType type)
            {
                Caller         = caller;
                ConnectionType = type;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventConnectionRequest
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance   Caller;
            public ConnectionRequest Request;

            public Arguments(NetworkInstance caller, ConnectionRequest request)
            {
                Caller  = caller;
                Request = request;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventPeerConnected
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public NetPeerInstance PeerInstance;

            public Arguments(NetworkInstance caller, NetPeerInstance peerInstance)
            {
                Caller       = caller;
                PeerInstance = peerInstance;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventPeerDisconnected
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public NetPeer         Peer;
            public DisconnectInfo  DisconnectInfo;

            public Arguments(NetworkInstance caller, NetPeer peer, DisconnectInfo disconnectInfo)
            {
                Caller         = caller;
                Peer           = peer;
                DisconnectInfo = disconnectInfo;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventNetworkError
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public IPEndPoint      EndPoint;
            public int             ErrorCode;

            public Arguments(NetworkInstance caller, IPEndPoint endPoint, int errorCode)
            {
                Caller    = caller;
                EndPoint  = endPoint;
                ErrorCode = errorCode;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventReceiveUnconnectedData
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance        Caller;
            public IPEndPoint             EndPoint;
            public NetDataReader          Reader;
            public UnconnectedMessageType MessageType;

            public Arguments(NetworkInstance caller, IPEndPoint endPoint, NetDataReader reader, UnconnectedMessageType messageType)
            {
                Caller      = caller;
                EndPoint    = endPoint;
                Reader      = reader;
                MessageType = messageType;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventReceiveData
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public NetPeerInstance PeerInstance;
            public MessageReader   Reader;

            public Arguments(NetworkInstance caller, NetPeerInstance peerInstance, MessageReader reader)
            {
                Caller       = caller;
                PeerInstance = peerInstance;
                Reader       = reader;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventPeerReceiveBroadcastData
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetworkInstance Caller;
            public NetPeerInstance PeerInstance;

            public Arguments(NetworkInstance caller, NetPeerInstance peerInstance)
            {
                Caller       = caller;
                PeerInstance = peerInstance;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }

    public abstract class EventUserStatusChange
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetPeerInstance Caller;
            public NetUser         User;
            public StatusChange    Change;

            public Arguments(NetPeerInstance caller, NetUser user, StatusChange change)
            {
                Caller = caller;
                User   = user;
                Change = change;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }*/
}
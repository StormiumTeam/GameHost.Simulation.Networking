using System.Net;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;

namespace package.stormiumteam.networking
{
    public interface INetOnInstanceReady : IAppEvent
    {
        void Callback(NetworkInstance caller, ConnectionType connectionType);
    }

    public interface INetConnectionRequestEvent : IAppEvent
    {
        void Callback(NetworkInstance caller, ConnectionRequest request);
    }

    public interface INetPeerConnectedEvent : IAppEvent
    {
        void Callback(NetworkInstance caller, NetPeerInstance peerInstance);
    }

    public interface INetSentAllBroadcastedEventToPeer : IAppEvent
    {
        void Callback(NetworkInstance caller, NetPeerInstance peerInstance);
    }

    public interface INetPeerDisconnectedEvent : IAppEvent
    {
        void Callback(NetworkInstance caller, NetPeer peer, DisconnectInfo disconnectInfo);
    }

    public interface INetErrorEvent : IAppEvent
    {
        void Callback(NetworkInstance caller, IPEndPoint ip, int errorCode);
    }

    public interface INetReceiveReceiveUnconnectedEvent : IAppEvent
    {
        void Callback(NetworkInstance caller, IPEndPoint ip, NetDataReader reader, UnconnectedMessageType messageType);
    }

    public interface INetOnNewMessage : IAppEvent
    {
        void Callback(NetPeerInstance caller, MessageReader reader);
    }

    public interface INetOnUserStatusChange : IAppEvent
    {
        void Callback(NetPeerInstance caller, NetUser user, StatusChange change);
    }
}
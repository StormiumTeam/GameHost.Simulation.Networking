using System.Net;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    /// <summary>
    /// Type of message that you receive in OnNetworkReceiveUnconnected event
    /// </summary>
    public enum UnconnectedMessageType
    {
        BasicMessage,
        DiscoveryRequest,
        DiscoveryResponse
    }

    /// <summary>
    /// Disconnect reason that you receive in OnPeerDisconnected event
    /// </summary>
    public enum DisconnectReason
    {
        SocketReceiveError,
        ConnectionFailed,
        Timeout,
        SocketSendError,
        RemoteConnectionClose,
        DisconnectPeerCalled,
        ConnectionRejected
    }

    /// <summary>
    /// Additional information about disconnection
    /// </summary>
    public struct DisconnectInfo
    {
        /// <summary>
        /// Additional info why peer disconnected
        /// </summary>
        public DisconnectReason Reason;

        /// <summary>
        /// Error code (if reason is SocketSendError or SocketReceiveError)
        /// </summary>
        public int SocketErrorCode;

        /// <summary>
        /// Additional data that can be accessed (only if reason is RemoteConnectionClose)
        /// </summary>
        public NetDataReader AdditionalData;
    }

    public interface INetEventListener
    {
        /// <summary>
        /// New remote peer connected to host, or client connected to remote host
        /// </summary>
        /// <param name="peer">Connected peer object</param>
        void OnPeerConnected(NetPeer peer);

        /// <summary>
        /// Peer disconnected
        /// </summary>
        /// <param name="peer">disconnected peer</param>
        /// <param name="disconnectInfo">additional info about reason, errorCode or data received with disconnect message</param>
        void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo);

        /// <summary>
        /// Network error (on send or receive)
        /// </summary>
        /// <param name="endPoint">From endPoint (can be null)</param>
        /// <param name="socketErrorCode">Socket error code</param>
        void OnNetworkError(IPEndPoint endPoint, int socketErrorCode);

        /// <summary>
        /// Received some data
        /// </summary>
        /// <param name="peer">From peer</param>
        /// <param name="reader">DataReader containing all received data</param>
        /// <param name="deliveryMethod">Type of received packet</param>
        void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod);

        /// <summary>
        /// Received unconnected message
        /// </summary>
        /// <param name="remoteEndPoint">From address (IP and Port)</param>
        /// <param name="reader">Message data</param>
        /// <param name="messageType">Message type (simple, discovery request or responce)</param>
        void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType);

        /// <summary>
        /// Latency information updated
        /// </summary>
        /// <param name="peer">Peer with updated latency</param>
        /// <param name="latency">latency value in milliseconds</param>
        void OnNetworkLatencyUpdate(NetPeer peer, int latency);

        /// <summary>
        /// On peer connection requested
        /// </summary>
        /// <param name="request">Request information (EndPoint, internal id, additional data)</param>
        void OnConnectionRequest(ConnectionRequest request);
    }

    public class EventBasedNetListener : INetEventListener
    {
        public delegate void OnPeerConnected(NetPeer peer);
        public delegate void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo);
        public delegate void OnNetworkError(IPEndPoint endPoint, int socketErrorCode);
        public delegate void OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod);
        public delegate void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType);
        public delegate void OnNetworkLatencyUpdate(NetPeer peer, int latency);

        public delegate void OnConnectionRequest(ConnectionRequest request);

        public event OnPeerConnected PeerConnectedEvent;
        public event OnPeerDisconnected PeerDisconnectedEvent;
        public event OnNetworkError NetworkErrorEvent;
        public event OnNetworkReceive NetworkReceiveEvent;
        public event OnNetworkReceiveUnconnected NetworkReceiveUnconnectedEvent;
        public event OnNetworkLatencyUpdate NetworkLatencyUpdateEvent;
        public event OnConnectionRequest ConnectionRequestEvent;

        void INetEventListener.OnPeerConnected(NetPeer peer)
        {
            if (PeerConnectedEvent != null)
                PeerConnectedEvent(peer);
        }

        void INetEventListener.OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            if (PeerDisconnectedEvent != null)
                PeerDisconnectedEvent(peer, disconnectInfo);
        }

        void INetEventListener.OnNetworkError(IPEndPoint endPoint, int socketErrorCode)
        {
            if (NetworkErrorEvent != null)
                NetworkErrorEvent(endPoint, socketErrorCode);
        }

        void INetEventListener.OnNetworkReceive(NetPeer peer, NetDataReader reader, DeliveryMethod deliveryMethod)
        {
            if (NetworkReceiveEvent != null)
                NetworkReceiveEvent(peer, reader, deliveryMethod);
        }

        void INetEventListener.OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint, NetDataReader reader, UnconnectedMessageType messageType)
        {
            if (NetworkReceiveUnconnectedEvent != null)
                NetworkReceiveUnconnectedEvent(remoteEndPoint, reader, messageType);
        }

        void INetEventListener.OnNetworkLatencyUpdate(NetPeer peer, int latency)
        {
            if (NetworkLatencyUpdateEvent != null)
                NetworkLatencyUpdateEvent(peer, latency);
        }

        void INetEventListener.OnConnectionRequest(ConnectionRequest request)
        {
            if (ConnectionRequestEvent != null)
                ConnectionRequestEvent(request);
        }
    }
}

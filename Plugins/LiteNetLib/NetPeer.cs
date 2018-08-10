#if DEBUG
#define STATS_ENABLED
#endif
using System;
using System.Collections.Generic;
using System.Net;
using LiteNetLib.Utils;

namespace LiteNetLib
{
    /// <summary>
    /// Peer connection state
    /// </summary>
    [Flags]
    public enum ConnectionState : byte
    {
        Incoming          = 1 << 1,
        InProgress        = 1 << 2,
        Connected         = 1 << 3,
        ShutdownRequested = 1 << 4,
        Disconnected      = 1 << 5,
        Any = Incoming | InProgress | Connected | ShutdownRequested
    }

    internal enum ConnectRequestResult
    {
        None,
        P2PConnection, //when peer connecting
        Reconnection,  //when peer was connected
        NewConnection  //when peer was disconnected
    }

    internal enum DisconnectResult
    {
        None,
        Reject,
        Disconnect
    }

    /// <summary>
    /// Network peer. Main purpose is sending messages to specific peer.
    /// </summary>
    public sealed class NetPeer
    {
        //Ping and RTT
        private int _ping;
        private int _rtt;
        private int _avgRtt;
        private int _rttCount;
        private ushort _pingSequence;
        private ushort _remotePingSequence;
        private double _resendDelay = 27.0;

        private int _pingSendTimer;
        private const int RttResetDelay = 1000;
        private int _rttResetTimer;

        private DateTime _pingTimeStart;
        private int _timeSinceLastPacket;

        //Common            
        private readonly IPEndPoint _remoteEndPoint;
        private readonly NetManager _netManager;
        private readonly NetPacketPool _packetPool;
        private readonly object _flushLock = new object();
        private readonly object _sendLock = new object();

        internal NetPeer NextPeer;
        internal NetPeer PrevPeer;

        internal byte ConnectionNum
        {
            get { return _connectNum; }
            private set
            {
                _connectNum = value;
                _mergeData.ConnectionNumber = value;
                _pingPacket.ConnectionNumber = value;
                _pongPacket.ConnectionNumber = value;
            }
        }
 
        //Channels
        private ReliableChannel _reliableOrderedChannel;
        private ReliableChannel _reliableUnorderedChannel;
        private SequencedChannel _sequencedChannel;
        private SimpleChannel _unreliableChannel;
        private SequencedChannel _reliableSequencedChannel;

        //MTU
        private int _mtu = NetConstants.PossibleMtu[0];
        private int _mtuIdx;
        private bool _finishMtu;
        private int _mtuCheckTimer;
        private int _mtuCheckAttempts;
        private const int MtuCheckDelay = 1000;
        private const int MaxMtuCheckAttempts = 4;
        private readonly object _mtuMutex = new object();

        //Fragment
        private class IncomingFragments
        {
            public NetPacket[] Fragments;
            public int ReceivedCount;
            public int TotalSize;
        }
        private ushort _fragmentId;
        private Dictionary<ushort, IncomingFragments> _holdedFragments;

        //Merging
        private readonly NetPacket _mergeData;
        private int _mergePos;
        private int _mergeCount;

        //Connection
        private int _connectAttempts;
        private int _connectTimer;
        private long _connectId;
        private byte _connectNum;
        private ConnectionState _connectionState;
        private NetPacket _shutdownPacket;
        private readonly NetPacket _pingPacket;
        private readonly NetPacket _pongPacket;
        private readonly NetPacket _connectRequestPacket;
        private NetPacket _connectAcceptPacket;

        /// <summary>
        /// Current connection state
        /// </summary>
        public ConnectionState ConnectionState { get { return _connectionState; } }

        /// <summary>
        /// Connection id for internal purposes, but can be used as key in your dictionary of peers
        /// </summary>
        public long ConnectId { get { return _connectId; } }

        /// <summary>
        /// Peer ip address and port
        /// </summary>
        public IPEndPoint EndPoint { get { return _remoteEndPoint; } }

        /// <summary>
        /// Current ping in milliseconds
        /// </summary>
        public int Ping { get { return _ping; } }

        /// <summary>
        /// Current MTU - Maximum Transfer Unit ( maximum udp packet size without fragmentation )
        /// </summary>
        public int Mtu { get { return _mtu; } }

        /// <summary>
        /// Time since last packet received (including internal library packets)
        /// </summary>
        public int TimeSinceLastPacket { get { return _timeSinceLastPacket; } }

        /// <summary>
        /// Peer parent NetManager
        /// </summary>
        public NetManager NetManager { get { return _netManager; } }

        public int PacketsCountInReliableQueue { get { return _reliableUnorderedChannel.PacketsInQueue; } }

        public int PacketsCountInReliableOrderedQueue { get { return _reliableOrderedChannel.PacketsInQueue; } }

        internal double ResendDelay { get { return _resendDelay; } }

        /// <summary>
		/// Application defined object containing data about the connection
		/// </summary>
        public object Tag;

        /// <summary>
        /// Statistics of peer connection
        /// </summary>
        public readonly NetStatistics Statistics;

        //incoming connection constructor
        internal NetPeer(NetManager netManager, IPEndPoint remoteEndPoint)
        {
            Statistics = new NetStatistics();
            _packetPool = netManager.NetPacketPool;
            _netManager = netManager;
            _remoteEndPoint = remoteEndPoint;
            _connectionState = ConnectionState.Incoming;
            _mergeData = new NetPacket(PacketProperty.Merged, NetConstants.MaxPacketSize);
            _pongPacket = new NetPacket(PacketProperty.Pong, 0);
            _pingPacket = new NetPacket(PacketProperty.Ping, 0);
        }

        //for low memory consumption
        private void Initialize()
        {
            _reliableOrderedChannel = new ReliableChannel(this, true);
            _reliableUnorderedChannel = new ReliableChannel(this, false);
            _sequencedChannel = new SequencedChannel(this, false);
            _unreliableChannel = new SimpleChannel(this);
            _reliableSequencedChannel = new SequencedChannel(this, true);
            _holdedFragments = new Dictionary<ushort, IncomingFragments>();
        }

        //"Connect to" constructor
        internal NetPeer(NetManager netManager, IPEndPoint remoteEndPoint, byte connectNum, NetDataWriter connectData) : this(netManager, remoteEndPoint)
        {
            Initialize();
            _connectId = DateTime.UtcNow.Ticks;
            _connectionState = ConnectionState.InProgress;
            ConnectionNum = connectNum;

            //Make initial packet
            _connectRequestPacket = NetConnectRequestPacket.Make(connectData, _connectId);
            _connectRequestPacket.ConnectionNumber = connectNum;

            //Send request
            _netManager.SendRaw(_connectRequestPacket, _remoteEndPoint);

            NetUtils.DebugWrite(ConsoleColor.Cyan, "[CC] ConnectId: {0}, ConnectNum: {1}", _connectId, connectNum);
        }

        //"Accept" incoming constructor
        internal void Accept(long connectId, byte connectNum)
        {
            Initialize();
            _connectId = connectId;
            _connectionState = ConnectionState.Connected;
            ConnectionNum = connectNum;

            //Make initial packet
            _connectAcceptPacket = NetConnectAcceptPacket.Make(_connectId, connectNum, false);
            //Send
            _netManager.SendRaw(_connectAcceptPacket, _remoteEndPoint);

            NetUtils.DebugWrite(ConsoleColor.Cyan, "[CC] ConnectId: {0}", _connectId);
        }

        internal bool ProcessConnectAccept(NetConnectAcceptPacket packet)
        {
            if (_connectionState != ConnectionState.InProgress)
                return false;

            //check connection id
            if (packet.ConnectionId != _connectId)
            {
                NetUtils.DebugWrite(ConsoleColor.Cyan, "[NC] Invalid connectId: {0}", _connectId);
                return false;
            }
            //check connect num
            ConnectionNum = packet.ConnectionNumber;

            NetUtils.DebugWrite(ConsoleColor.Cyan, "[NC] Received connection accept");
            _timeSinceLastPacket = 0;
            _connectionState = ConnectionState.Connected;
            return true;
        }

        private static PacketProperty SendOptionsToProperty(DeliveryMethod options)
        {
            switch (options)
            {
                case DeliveryMethod.ReliableUnordered:
                    return PacketProperty.ReliableUnordered;
                case DeliveryMethod.Sequenced:
                    return PacketProperty.Sequenced;
                case DeliveryMethod.ReliableOrdered:
                    return PacketProperty.ReliableOrdered;
                case DeliveryMethod.ReliableSequenced:
                    return PacketProperty.ReliableSequenced;
                default:
                    return PacketProperty.Unreliable;
            }
        }

        /// <summary>
        /// Gets maximum size of packet that will be not fragmented.
        /// </summary>
        /// <param name="options">Type of packet that you want send</param>
        /// <returns>size in bytes</returns>
        public int GetMaxSinglePacketSize(DeliveryMethod options)
        {
            return _mtu - NetPacket.GetHeaderSize(SendOptionsToProperty(options));
        }

        /// <summary>
        /// Send data to peer
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <exception cref="TooBigPacketException">
        ///     If size exceeds maximum limit:<para/>
        ///     MTU - headerSize bytes for Unreliable<para/>
        ///     Fragment count exceeded ushort.MaxValue<para/>
        /// </exception>
        public void Send(byte[] data, DeliveryMethod options)
        {
            Send(data, 0, data.Length, options);
        }

        /// <summary>
        /// Send data to peer
        /// </summary>
        /// <param name="dataWriter">DataWriter with data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <exception cref="TooBigPacketException">
        ///     If size exceeds maximum limit:<para/>
        ///     MTU - headerSize bytes for Unreliable<para/>
        ///     Fragment count exceeded ushort.MaxValue<para/>
        /// </exception>
        public void Send(NetDataWriter dataWriter, DeliveryMethod options)
        {
            Send(dataWriter.Data, 0, dataWriter.Length, options);
        }

        /// <summary>
        /// Send data to peer
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="start">Start of data</param>
        /// <param name="length">Length of data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <exception cref="TooBigPacketException">
        ///     If size exceeds maximum limit:<para/>
        ///     MTU - headerSize bytes for Unreliable<para/>
        ///     Fragment count exceeded ushort.MaxValue<para/>
        /// </exception>
        public void Send(byte[] data, int start, int length, DeliveryMethod options)
        {
            if (_connectionState == ConnectionState.ShutdownRequested || 
                _connectionState == ConnectionState.Disconnected)
            {
                return;
            }
            //Prepare
            PacketProperty property = SendOptionsToProperty(options);
            NetUtils.DebugWrite("[RS]Packet: " + property);

            //Select channel
            BaseChannel channel;
            switch (property)
            {
                case PacketProperty.ReliableUnordered:
                    channel = _reliableUnorderedChannel;
                    break;
                case PacketProperty.Sequenced:
                    channel = _sequencedChannel;
                    break;
                case PacketProperty.ReliableOrdered:
                    channel = _reliableOrderedChannel;
                    break;
                case PacketProperty.Unreliable:
                    channel = _unreliableChannel;
                    break;
                case PacketProperty.ReliableSequenced:
                    channel = _reliableSequencedChannel;
                    break;
                default:
                    throw new InvalidPacketException("Unknown packet property: " + property);
            }

            //Check fragmentation
            int headerSize = NetPacket.GetHeaderSize(property);
            //Save mtu for multithread
            int mtu = _mtu;
            if (length + headerSize > mtu)
            {
                if (options == DeliveryMethod.Sequenced || 
                    options == DeliveryMethod.Unreliable ||
                    options == DeliveryMethod.ReliableSequenced)
                {
                    throw new TooBigPacketException("Unreliable packet size exceeded maximum of " + (_mtu - headerSize) + " bytes");
                }
                
                int packetFullSize = mtu - headerSize;
                int packetDataSize = packetFullSize - NetConstants.FragmentHeaderSize;

                int fullPacketsCount = length / packetDataSize;
                int lastPacketSize = length % packetDataSize;
                int totalPackets = fullPacketsCount + (lastPacketSize == 0 ? 0 : 1);

                NetUtils.DebugWrite("FragmentSend:\n" +
                           " MTU: {0}\n" +
                           " headerSize: {1}\n" +
                           " packetFullSize: {2}\n" +
                           " packetDataSize: {3}\n" +
                           " fullPacketsCount: {4}\n" +
                           " lastPacketSize: {5}\n" +
                           " totalPackets: {6}",
                    mtu, headerSize, packetFullSize, packetDataSize, fullPacketsCount, lastPacketSize, totalPackets);

                if (totalPackets > ushort.MaxValue)
                {
                    throw new TooBigPacketException("Data was split in " + totalPackets + " fragments, which exceeds " + ushort.MaxValue);
                }

                int dataOffset = headerSize + NetConstants.FragmentHeaderSize;

                lock (_sendLock)
                {
                    for (ushort i = 0; i < fullPacketsCount; i++)
                    {
                        NetPacket p = _packetPool.GetWithProperty(property, packetFullSize);
                        p.FragmentId = _fragmentId;
                        p.FragmentPart = i;
                        p.FragmentsTotal = (ushort)totalPackets;
                        p.MarkFragmented();
                        Buffer.BlockCopy(data, i * packetDataSize, p.RawData, dataOffset, packetDataSize);
                        channel.AddToQueue(p);
                    }
                    if (lastPacketSize > 0)
                    {
                        NetPacket p = _packetPool.GetWithProperty(property, lastPacketSize + NetConstants.FragmentHeaderSize);
                        p.FragmentId = _fragmentId;
                        p.FragmentPart = (ushort)fullPacketsCount; //last
                        p.FragmentsTotal = (ushort)totalPackets;
                        p.MarkFragmented();
                        Buffer.BlockCopy(data, fullPacketsCount * packetDataSize, p.RawData, dataOffset, lastPacketSize);
                        channel.AddToQueue(p);
                    }
                    _fragmentId++;
                }
                return;
            }

            //Else just send
            NetPacket packet = _packetPool.GetWithData(property, data, start, length);
            channel.AddToQueue(packet);
        }

        public void Disconnect(byte[] data)
        {
            _netManager.DisconnectPeer(this, data);
        }

        public void Disconnect(NetDataWriter writer)
        {
            _netManager.DisconnectPeer(this, writer);
        }

        public void Disconnect(byte[] data, int start, int count)
        {
            _netManager.DisconnectPeer(this, data, start, count);
        }

        public void Disconnect()
        {
            _netManager.DisconnectPeer(this);
        }

        internal DisconnectResult ProcessDisconnect(NetPacket packet)
        {
            switch (_connectionState)
            {
                case ConnectionState.Connected:
                case ConnectionState.InProgress:
                    if (packet.Size >= 9 &&
                        BitConverter.ToInt64(packet.RawData, 1) == _connectId &&
                        packet.ConnectionNumber == _connectNum)
                    {
                        DisconnectResult result = _connectionState == ConnectionState.Connected 
                            ? DisconnectResult.Disconnect 
                            : DisconnectResult.Reject;

                        _connectionState = ConnectionState.Disconnected;
                        return result;
                    }
                    break;
            }
            return DisconnectResult.None;
        }

        internal bool Shutdown(byte[] data, int start, int length, bool force)
        {
            lock (this)
            {
                //trying to shutdown already disconnected
                if (_connectionState == ConnectionState.Disconnected ||
                    _connectionState == ConnectionState.ShutdownRequested)
                {
                    return false;
                }

                //don't send anything
                if (force)
                {
                    _connectionState = ConnectionState.Disconnected;
                    return true;
                }

                //reset time for reconnect protection
                _timeSinceLastPacket = 0;

                //send shitdown packet
                _shutdownPacket = new NetPacket(PacketProperty.Disconnect, 8 + length);
                FastBitConverter.GetBytes(_shutdownPacket.RawData, 1, _connectId);
                if (length + 8 >= _mtu)
                {
                    //Drop additional data
                    NetUtils.DebugWriteError("[Peer] Disconnect additional data size more than MTU - 8!");
                }
                else if (data != null && length > 0)
                {
                    Buffer.BlockCopy(data, start, _shutdownPacket.RawData, 9, length);
                }
                _connectionState = ConnectionState.ShutdownRequested;
                NetUtils.DebugWrite("[Peer] Send disconnect");
                _netManager.SendRaw(_shutdownPacket, _remoteEndPoint);
                return true;
            }
        }

        private void UpdateRoundTripTime(int roundTripTime)
        {
            //Calc average round trip time
            _rtt += roundTripTime;
            _rttCount++;
            _avgRtt = _rtt/_rttCount;

            //recalc resend delay
            double avgRtt = _avgRtt;
            if (avgRtt <= 0.0)
                avgRtt = 0.1;
            _resendDelay = 25 + (avgRtt * 2.1); // 25 ms + double rtt
        }

        internal void AddIncomingPacket(NetPacket p)
        {
            if (p.IsFragmented)
            {
                NetUtils.DebugWrite("Fragment. Id: {0}, Part: {1}, Total: {2}", p.FragmentId, p.FragmentPart, p.FragmentsTotal);
                //Get needed array from dictionary
                ushort packetFragId = p.FragmentId;
                IncomingFragments incomingFragments;
                if (!_holdedFragments.TryGetValue(packetFragId, out incomingFragments))
                {
                    incomingFragments = new IncomingFragments
                    {
                        Fragments = new NetPacket[p.FragmentsTotal]
                    };
                    _holdedFragments.Add(packetFragId, incomingFragments);
                }

                //Cache
                var fragments = incomingFragments.Fragments;

                //Error check
                if (p.FragmentPart >= fragments.Length || fragments[p.FragmentPart] != null)
                {
                    _packetPool.Recycle(p);
                    NetUtils.DebugWriteError("Invalid fragment packet");
                    return;
                }
                //Fill array
                fragments[p.FragmentPart] = p;

                //Increase received fragments count
                incomingFragments.ReceivedCount++;

                //Increase total size
                int dataOffset = p.GetHeaderSize() + NetConstants.FragmentHeaderSize;
                incomingFragments.TotalSize += p.Size - dataOffset;

                //Check for finish
                if (incomingFragments.ReceivedCount != fragments.Length)
                {
                    return;
                }

                NetUtils.DebugWrite("Received all fragments!");
                NetPacket resultingPacket = _packetPool.GetWithProperty( p.Property, incomingFragments.TotalSize );

                int resultingPacketOffset = resultingPacket.GetHeaderSize();
                int firstFragmentSize = fragments[0].Size - dataOffset;
                for (int i = 0; i < incomingFragments.ReceivedCount; i++)
                {
                    //Create resulting big packet
                    int fragmentSize = fragments[i].Size - dataOffset;
                    Buffer.BlockCopy(
                        fragments[i].RawData,
                        dataOffset,
                        resultingPacket.RawData,
                        resultingPacketOffset + firstFragmentSize * i,
                        fragmentSize);

                    //Free memory
                    _packetPool.Recycle(fragments[i]);
                    fragments[i] = null;
                }

                //Send to process
                _netManager.ReceiveFromPeer(resultingPacket, _remoteEndPoint);

                //Clear memory
                _packetPool.Recycle(resultingPacket);
                _holdedFragments.Remove(packetFragId);
            }
            else //Just simple packet
            {
                _netManager.ReceiveFromPeer(p, _remoteEndPoint);
                _packetPool.Recycle(p);
            }
        }

        private void ProcessMtuPacket(NetPacket packet)
        {
            if (packet.Size == 1 || 
                packet.RawData[1] >= NetConstants.PossibleMtu.Length)
                return;

            //MTU auto increase
            if (packet.Property == PacketProperty.MtuCheck)
            {
                if (packet.Size != NetConstants.PossibleMtu[packet.RawData[1]])
                {
                    return;
                }
                _mtuCheckAttempts = 0;
                NetUtils.DebugWrite("MTU check. Resend: " + packet.RawData[1]);
                var mtuOkPacket = _packetPool.GetWithProperty(PacketProperty.MtuOk, 1);
                mtuOkPacket.RawData[1] = packet.RawData[1];
                _netManager.SendRawAndRecycle(mtuOkPacket, _remoteEndPoint);
            }
            else if(packet.RawData[1] > _mtuIdx) //MtuOk
            {
                lock (_mtuMutex)
                {
                    _mtuIdx = packet.RawData[1];
                    _mtu = NetConstants.PossibleMtu[_mtuIdx];
                }
                //if maxed - finish.
                if (_mtuIdx == NetConstants.PossibleMtu.Length - 1)
                {
                    _finishMtu = true;
                }
                NetUtils.DebugWrite("MTU ok. Increase to: " + _mtu);
            }
        }

        internal ConnectRequestResult ProcessConnectRequest(NetConnectRequestPacket connRequest)
        {
            //current or new request
            switch (_connectionState)
            {
                //P2P case or just ID update
                case ConnectionState.InProgress:
                case ConnectionState.Incoming:
                    _connectionState = ConnectionState.Incoming;
                    //change connect id if newer
                    if (connRequest.ConnectionId >= _connectId)
                    {
                        //Change connect id
                        _connectId = connRequest.ConnectionId;
                        ConnectionNum = connRequest.ConnectionNumber;
                    }
                    return _connectionState == ConnectionState.InProgress 
                        ? ConnectRequestResult.P2PConnection 
                        : ConnectRequestResult.None;

                case ConnectionState.Connected:
                    //Old connect request
                    if (connRequest.ConnectionId == _connectId)
                    {
                        //just reply accept
                        _netManager.SendRaw(_connectAcceptPacket, _remoteEndPoint);
                    }
                    //New connect request
                    else if (connRequest.ConnectionId > _connectId)
                    {
                        return ConnectRequestResult.Reconnection;
                    }
                    break;

                case ConnectionState.Disconnected:
                case ConnectionState.ShutdownRequested:
                    if (connRequest.ConnectionId >= _connectId)
                    {
                        return ConnectRequestResult.NewConnection;
                    }
                    break;
            }
            return ConnectRequestResult.None;
        }

        //Process incoming packet
        internal void ProcessPacket(NetPacket packet)
        {
            _timeSinceLastPacket = 0;
            if (packet.ConnectionNumber != _connectNum && 
                packet.Property != PacketProperty.ShutdownOk) //withou connectionNum
            {
                NetUtils.DebugWrite(ConsoleColor.Red, "[RR]Old packet");
                _packetPool.Recycle(packet);
                return;
            }

            NetUtils.DebugWrite("[RR]PacketProperty: {0}", packet.Property);
            switch (packet.Property)
            {
                case PacketProperty.Merged:
                    int pos = NetConstants.HeaderSize;
                    while (pos < packet.Size)
                    {
                        ushort size = BitConverter.ToUInt16(packet.RawData, pos);
                        pos += 2;
                        NetPacket mergedPacket = _packetPool.GetPacket(size, false);
                        if (!mergedPacket.FromBytes(packet.RawData, pos, size))
                        {
                            _packetPool.Recycle(packet);
                            break;
                        }
                        pos += size;
                        ProcessPacket(mergedPacket);
                    }
                    break;
                //If we get ping, send pong
                case PacketProperty.Ping:
                    if (NetUtils.RelativeSequenceNumber(packet.Sequence, _remotePingSequence) < 0)
                    {
                        _packetPool.Recycle(packet);
                        break;
                    }
                    NetUtils.DebugWrite("[PP]Ping receive, send pong");
                    _remotePingSequence = packet.Sequence;
                    _packetPool.Recycle(packet);

                    //send
                    _pongPacket.Sequence = _remotePingSequence;
                    _netManager.SendRaw(_pongPacket, _remoteEndPoint);
                    break;

                //If we get pong, calculate ping time and rtt
                case PacketProperty.Pong:
                    if (NetUtils.RelativeSequenceNumber(packet.Sequence, _pingSequence) < 0)
                    {
                        _packetPool.Recycle(packet);
                        break;
                    }
                    _pingSequence = packet.Sequence;
                    int rtt = (int)(DateTime.UtcNow - _pingTimeStart).TotalMilliseconds;
                    UpdateRoundTripTime(rtt);
                    NetUtils.DebugWrite("[PP]Ping: {0}", rtt);
                    _packetPool.Recycle(packet);
                    break;

                //Process ack
                case PacketProperty.AckReliable:
                    _reliableUnorderedChannel.ProcessAck(packet);
                    _packetPool.Recycle(packet);
                    break;

                case PacketProperty.AckReliableOrdered:
                    _reliableOrderedChannel.ProcessAck(packet);
                    _packetPool.Recycle(packet);
                    break;

                //Process in order packets
                case PacketProperty.Sequenced:
                    _sequencedChannel.ProcessPacket(packet);
                    break;

                case PacketProperty.ReliableUnordered:
                    _reliableUnorderedChannel.ProcessPacket(packet);
                    break;

                case PacketProperty.ReliableOrdered:
                    _reliableOrderedChannel.ProcessPacket(packet);
                    break;

                case PacketProperty.ReliableSequenced:
                    _reliableSequencedChannel.ProcessPacket(packet);
                    break;

                case PacketProperty.AckReliableSequenced:
                    _reliableSequencedChannel.ProcessAck(packet);
                    _packetPool.Recycle(packet);
                    break;

                //Simple packet without acks
                case PacketProperty.Unreliable:
                    AddIncomingPacket(packet);
                    return;

                case PacketProperty.MtuCheck:
                case PacketProperty.MtuOk:
                    ProcessMtuPacket(packet);
                    break;

                case PacketProperty.ShutdownOk:
                    if(_connectionState == ConnectionState.ShutdownRequested)
                        _connectionState = ConnectionState.Disconnected;
                    _packetPool.Recycle(packet);
                    break;            
                
                default:
                    NetUtils.DebugWriteError("Error! Unexpected packet type: " + packet.Property);
                    break;
            }
        }

        internal void SendRawData(NetPacket packet)
        {
            packet.ConnectionNumber = _connectNum;
            //2 - merge byte + minimal packet size + datalen(ushort)
            if (_netManager.MergeEnabled && _mergePos + packet.Size + NetConstants.HeaderSize*2 + 2 < _mtu)
            {
                FastBitConverter.GetBytes(_mergeData.RawData, _mergePos + NetConstants.HeaderSize, (ushort)packet.Size);
                Buffer.BlockCopy(packet.RawData, 0, _mergeData.RawData, _mergePos + NetConstants.HeaderSize + 2, packet.Size);
                _mergePos += packet.Size + 2;
                _mergeCount++;

                //DebugWriteForce("Merged: " + _mergePos + "/" + (_mtu - 2) + ", count: " + _mergeCount);
                return;
            }

            NetUtils.DebugWrite(ConsoleColor.DarkYellow, "[P]SendingPacket: " + packet.Property);
            _netManager.SendRaw(packet, _remoteEndPoint);
#if STATS_ENABLED
            Statistics.PacketsSent++;
            Statistics.BytesSent += (ulong)packet.Size;
#endif
        }

        /// <summary>
        /// Flush all queued packets
        /// </summary>
        public void Flush()
        {
            lock (_flushLock)
            {
                _reliableOrderedChannel.SendNextPackets();
                _reliableUnorderedChannel.SendNextPackets();
                _reliableSequencedChannel.SendNextPackets();
                _sequencedChannel.SendNextPackets();
                _unreliableChannel.SendNextPackets();

                //If merging enabled
                if (_mergePos > 0)
                {
                    if (_mergeCount > 1)
                    {
                        NetUtils.DebugWrite("[P]Send merged: " + _mergePos + ", count: " + _mergeCount);
                        _netManager.SendRaw(_mergeData.RawData, 0, NetConstants.HeaderSize + _mergePos, _remoteEndPoint);
#if STATS_ENABLED
                        Statistics.PacketsSent++;
                        Statistics.BytesSent += (ulong)(NetConstants.HeaderSize + _mergePos);
#endif
                    }
                    else
                    {
                        //Send without length information and merging
                        _netManager.SendRaw(_mergeData.RawData, NetConstants.HeaderSize + 2, _mergePos - 2, _remoteEndPoint);
#if STATS_ENABLED
                        Statistics.PacketsSent++;
                        Statistics.BytesSent += (ulong)(_mergePos - 2);
#endif
                    }
                    _mergePos = 0;
                    _mergeCount = 0;
                }
            }
        }

        internal void Update(int deltaTime)
        {
            _timeSinceLastPacket += deltaTime;
            switch (_connectionState)
            {
                case ConnectionState.Connected:
                    if (_timeSinceLastPacket > _netManager.DisconnectTimeout)
                    {
                        NetUtils.DebugWrite(
                            "[UPDATE] Disconnect by timeout: {0} > {1}",
                            _timeSinceLastPacket,
                            _netManager.DisconnectTimeout);
                        _netManager.DisconnectPeer(this, DisconnectReason.Timeout, 0, true, null, 0, 0);
                        return;
                    }
                    break;

                case ConnectionState.ShutdownRequested:
                    if (_timeSinceLastPacket > _netManager.DisconnectTimeout)
                        _connectionState = ConnectionState.Disconnected;
                    else
                        _netManager.SendRaw(_shutdownPacket, _remoteEndPoint);
                    return;

                case ConnectionState.InProgress:
                    _connectTimer += deltaTime;
                    if (_connectTimer > _netManager.ReconnectDelay)
                    {
                        _connectTimer = 0;
                        _connectAttempts++;
                        if (_connectAttempts > _netManager.MaxConnectAttempts)
                        {
                            _netManager.DisconnectPeer(this, DisconnectReason.ConnectionFailed, 0, true, null, 0, 0);
                            return;
                        }

                        //else send connect again
                        _netManager.SendRaw(_connectRequestPacket, _remoteEndPoint);
                    }
                    return;

                case ConnectionState.Disconnected:
                case ConnectionState.Incoming:
                    return;
            }

            //Send ping
            _pingSendTimer += deltaTime;
            if (_pingSendTimer >= _netManager.PingInterval)
            {
                NetUtils.DebugWrite("[PP] Send ping...");

                //reset timer
                _pingSendTimer = 0;

                //send ping
                _pingPacket.Sequence = _pingSequence;
                SendRawData(_pingPacket);

                //reset timer
                _pingTimeStart = DateTime.UtcNow;
            }

            //RTT - round trip time
            _rttResetTimer += deltaTime;
            if (_rttResetTimer >= RttResetDelay)
            {
                _rttResetTimer = 0;
                //Rtt update
                _rtt = _avgRtt;
                _ping = _avgRtt;
                _netManager.ConnectionLatencyUpdated(this, _ping);
                _rttCount = 1;
            }

            //MTU - Maximum transmission unit
            if (!_finishMtu)
            {
                _mtuCheckTimer += deltaTime;
                if (_mtuCheckTimer >= MtuCheckDelay)
                {
                    _mtuCheckTimer = 0;
                    _mtuCheckAttempts++;
                    if (_mtuCheckAttempts >= MaxMtuCheckAttempts)
                    {
                        _finishMtu = true;
                    }
                    else
                    {
                        lock (_mtuMutex)
                        {
                            //Send increased packet
                            if (_mtuIdx < NetConstants.PossibleMtu.Length - 1)
                            {
                                int newMtu = NetConstants.PossibleMtu[_mtuIdx + 1] - NetConstants.HeaderSize;
                                var p = _packetPool.GetWithProperty(PacketProperty.MtuCheck, newMtu);
                                p.RawData[1] = (byte)(_mtuIdx + 1);

                                //Must check result for MTU fix
                                if (!_netManager.SendRawAndRecycle(p, _remoteEndPoint))
                                {
                                    _finishMtu = true;
                                }
                            }
                        }
                    }
                }
            }
            //MTU - end
            //Pending send
            Flush();
        }

        //For channels
        internal void Recycle(NetPacket packet)
        {
            _packetPool.Recycle(packet);
        }
    }
}

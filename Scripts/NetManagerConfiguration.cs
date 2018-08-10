namespace package.stormiumteam.networking
{
    public struct NetManagerConfiguration
    {
        /// <summary>
        /// Enable messages receiving without connection. (with SendUnconnectedMessage method)
        /// </summary>
        public bool UnconnectedMessagesEnabled;

        /// <summary>
        /// Enable nat punch messages
        /// </summary>
        public bool NatPunchEnabled;

        /// <summary>
        /// Library logic update and send period in milliseconds
        /// </summary>
        public int UpdateTime;

        /// <summary>
        /// Interval for latency detection and checking connection
        /// </summary>
        public int PingInterval;

        /// <summary>
        /// If NetManager doesn't receive any packet from remote peer during this time then connection will be closed
        /// (including library internal keepalive packets)
        /// </summary>
        public int DisconnectTimeout;

        /// <summary>
        /// Simulate packet loss by dropping random amout of packets. (Works only in DEBUG mode)
        /// </summary>
        public bool SimulatePacketLoss;

        /// <summary>
        /// Simulate latency by holding packets for random time. (Works only in DEBUG mode)
        /// </summary>
        public bool SimulateLatency;

        /// <summary>
        /// Chance of packet loss when simulation enabled. value in percents (1 - 100).
        /// </summary>
        public int SimulationPacketLossChance;

        /// <summary>
        /// Minimum simulated latency
        /// </summary>
        public int SimulationMinLatency;

        /// <summary>
        /// Maximum simulated latency
        /// </summary>
        public int SimulationMaxLatency;

        /// <summary>
        /// Experimental feature. Events automatically will be called without PollEvents method from another thread
        /// </summary>
        public bool UnsyncedEvents;

        /// <summary>
        /// Allows receive DiscoveryRequests
        /// </summary>
        public bool DiscoveryEnabled;

        /// <summary>
        /// Merge small packets into one before sending to reduce outgoing packets count. (May increase a bit outgoing data size)
        /// </summary>
        public bool MergeEnabled;

        /// <summary>
        /// Delay betwen initial connection attempts
        /// </summary>
        public int ReconnectDelay;

        /// <summary>
        /// Maximum connection attempts before client stops and call disconnect event.
        /// </summary>
        public int MaxConnectAttempts;

        /// <summary>
        /// Enables socket option "ReuseAddress" for specific purposes
        /// </summary>
        public bool ReuseAddress;

        public void @default()
        {
            UnconnectedMessagesEnabled = false;
            NatPunchEnabled            = false;
            UpdateTime                 = 15;
            PingInterval               = 1000;
            DisconnectTimeout          = 5000;
            SimulatePacketLoss         = false;
            SimulateLatency            = false;
            SimulationPacketLossChance = 10;
            SimulationMinLatency       = 30;
            SimulationMaxLatency       = 100;
            UnsyncedEvents             = false;
            DiscoveryEnabled           = false;
            MergeEnabled               = false;
            ReconnectDelay             = 500;
            MaxConnectAttempts         = 10;
            ReuseAddress               = false;
        }
    }
}
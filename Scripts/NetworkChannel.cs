using DefaultNamespace;
using LiteNetLib;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public struct NetworkChannelIdent
    {
        public int    Port;
        public string Id;

        public bool Equals(NetworkChannelIdent channel) => Id == channel.Id && Port == channel.Port;
        public bool Equals(NetworkChannel      channel) => Equals(channel.Data);
        public bool Equals(NetworkChannelData  data)    => Id == data.Id && Port == data.Port;

        public NetworkChannelIdent(string id, int port)
        {
            Id   = id;
            Port = port;
        }

        public bool IsMain() => Id == string.Intern(NetConstants.Channel_Main);
    }

    public struct NetworkChannelData
    {
        public NetworkInstance Creator;

        public string                   Id;
        public int                      Port;
        public DeliveryMethod           DefaultDelivery;
        public NetManagerConfiguration? Configuration;

        public NetworkChannelData(NetworkInstance creator, string id, int port, NetManagerConfiguration? configuration,
                                  DeliveryMethod  defaultDelivery)
        {
            Creator = creator;

            Id              = id;
            Port            = port;
            DefaultDelivery = defaultDelivery;
            Configuration   = configuration;
        }

        public bool IsMain() => Id == string.Intern(NetConstants.Channel_Main);
    }

    public class NetworkChannel
    {
        private NetworkChannelData m_Data;

        public NetworkChannelData Data  => m_Data;
        public NetworkInstance    Owner => m_Data.Creator;

        public EventBasedNetListener Listener { get; }
        public NetManager            Manager  { get; }

        public NetworkChannel(NetworkChannelData data, NetManager manager = null, EventBasedNetListener listener = null)
        {
            m_Data = data;

            if (manager == null)
            {
                Listener = new EventBasedNetListener();
                Manager  = new NetManager(Listener);

                if (data.Configuration.HasValue)
                {
                    var configuration = data.Configuration.Value;
                    Manager.UnconnectedMessagesEnabled = configuration.UnconnectedMessagesEnabled;
                    Manager.NatPunchEnabled            = configuration.NatPunchEnabled;
                    Manager.UpdateTime                 = configuration.UpdateTime;
                    Manager.PingInterval               = configuration.PingInterval;
                    Manager.DisconnectTimeout          = configuration.DisconnectTimeout;
                    Manager.SimulatePacketLoss         = configuration.SimulatePacketLoss;
                    Manager.SimulateLatency            = configuration.SimulateLatency;
                    Manager.SimulationPacketLossChance = configuration.SimulationPacketLossChance;
                    Manager.SimulationMinLatency       = configuration.SimulationMinLatency;
                    Manager.SimulationMaxLatency       = configuration.SimulationMaxLatency;
                    Manager.UnsyncedEvents             = configuration.UnsyncedEvents;
                    Manager.DiscoveryEnabled           = configuration.DiscoveryEnabled;
                    Manager.MergeEnabled               = configuration.MergeEnabled;
                    Manager.ReconnectDelay             = configuration.ReconnectDelay;
                    Manager.MaxConnectAttempts         = configuration.MaxConnectAttempts;
                    Manager.ReuseAddress               = configuration.ReuseAddress;
                }

                return;
            }

            Listener = listener;
            Manager  = manager;
        }

        public void StartListening()
        {
            Assert.IsTrue(Manager != null, "Manager != null, did you setup the channel correctly?");

            if (Manager.IsRunning)
                return;
            if (Data.Port < 0) // Mimic channel
                return;

            Manager.Start(m_Data.Port);

            if (m_Data.Creator.ConnectionInfo.ConnectionType != ConnectionType.Self) return;
            
            var newData = m_Data;
            newData.Port = Manager.LocalPort;

            m_Data = newData;
        }

        public void ConnectTo()
        {

        }

        public bool Equals(NetworkChannelIdent channel) => Is(channel.Id);
        public bool Equals(NetworkChannel      channel) => Is(channel.m_Data.Id);
        public bool Equals(NetworkChannelData  data)    => Is(data.Id);

        public bool Is(string id)
        {
            return m_Data.Creator.GetChannelManager().Is(this, string.Intern(id));
        }

        public bool Is(NetworkChannelIdent ident)
        {
            return m_Data.Creator.GetChannelManager().Is(this, string.Intern(ident.Id));
        }

        public bool IsMain()
        {
            return Is(NetConstants.Channel_Main);
        }
    }
}
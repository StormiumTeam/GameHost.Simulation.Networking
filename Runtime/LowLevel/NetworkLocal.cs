using System.Net;
using ENet;
using UnityEngine;

namespace package.stormiumteam.networking.Runtime.LowLevel
{
    public struct NetDriverConfiguration
    {
        public int  PeerLimit,         ChannelLimit;
        public uint IncomingBandwidth, OutgoingBandwidth;

        public static NetDriverConfiguration @default()
        {
            return new NetDriverConfiguration
            {
                PeerLimit         = 16,
                ChannelLimit      = (int) Library.maxChannelCount,
                IncomingBandwidth = 0,
                OutgoingBandwidth = 0
            };
        }
    }

    public enum NetDriverBindError
    {
        Success           = 0,
        NativeError       = 1,
        HostAlreadyBinded = 2,
        HostNull          = 3
    }

    public struct NetDriver
    {
        public Host                   Host;
        public NetDriverConfiguration Configuration;

        public NetDriver(NetDriverConfiguration configuration)
        {
            Configuration = configuration;
            Host          = new Host();
        }

        public NetDriver(int peerLimit)
        {
            if (peerLimit <= 0)
                Debug.LogWarning($"Peer limit was set to {peerLimit}!");
            
            Configuration           = NetDriverConfiguration.@default();
            Configuration.PeerLimit = peerLimit;
            Host                    = new Host();
        }

        public void UpdateConfiguration(NetDriverConfiguration newConfiguration)
        {
            if (!Host.IsSet)
            {
                // Update Channel Limit
                if (Configuration.ChannelLimit != newConfiguration.ChannelLimit)
                    Host.SetChannelLimit(newConfiguration.ChannelLimit);
                // Update Bandwith Limit
                if (Configuration.IncomingBandwidth != newConfiguration.IncomingBandwidth
                    || Configuration.OutgoingBandwidth != newConfiguration.OutgoingBandwidth)
                    Host.SetBandwidthLimit(newConfiguration.IncomingBandwidth, newConfiguration.OutgoingBandwidth);
            }

            Configuration = newConfiguration;
        }

        public NetDriverBindError Bind(IPEndPoint ipEndPoint)
        {
            return Bind(IPEndPointToENetAddress(ipEndPoint));
        }

        public NetDriverBindError Bind(Address address)
        {
            if (Host.IsSet)
                return NetDriverBindError.HostNull;
            
            Host.Create(address,
                Configuration.PeerLimit,
                Configuration.ChannelLimit,
                Configuration.IncomingBandwidth,
                Configuration.OutgoingBandwidth);

            Host.PreventConnections(true);

            return NetDriverBindError.Success;
        }

        public void Listen()
        {
            Host.PreventConnections(false);
        }

        public NetworkConnection Connect(IPEndPoint ipEndPoint)
        {
            Debug.Log("Connecting to " + ipEndPoint);
            return Connect(IPEndPointToENetAddress(ipEndPoint));
        }

        public NetworkConnection Connect(Address address)
        {
            Host.PreventConnections(false);
            return new NetworkConnection {Peer = Host.Connect(address)};
        }

        public Address IPEndPointToENetAddress(IPEndPoint ipEndPoint)
        {   
            var addr = new Address();
            var addrFailure = !addr.SetHost(ipEndPoint.Address.ToString());
            addr.Port = (ushort) ipEndPoint.Port;
            
            if (addrFailure) Debug.LogError("addrFailure: " + ipEndPoint.Address.ToString());
            
            return addr;
        }
    }
}
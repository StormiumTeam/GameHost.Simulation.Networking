using System.Net;
using ENet;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public struct NetDriverConfiguration
    {
        public int  PeerLimit,         ChannelLimit;
        public uint IncomingBandwidth, OutgoingBandwidth;

        public static NetDriverConfiguration @default()
        {
            return new NetDriverConfiguration
            {
                PeerLimit         = 32,
                ChannelLimit      = (int) Library.maxChannelCount,
                IncomingBandwidth = 0,
                OutgoingBandwidth = 0,
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

        public IPAddress LastBindedIPAddress;
        public Address LastBindedAddress;

        public NetDriver(NetDriverConfiguration configuration)
        {
            Configuration = configuration;
            Host          = new Host();
            
            LastBindedAddress = default(Address);
            LastBindedIPAddress = default(IPAddress);
        }

        public NetDriver(int peerLimit)
        {
            if (peerLimit <= 0)
                Debug.LogWarning($"Peer limit was set to {peerLimit}!");
            
            Configuration           = NetDriverConfiguration.@default();
            Configuration.PeerLimit = peerLimit;
            Host                    = new Host();
            
            LastBindedAddress   = default(Address);
            LastBindedIPAddress = default(IPAddress);
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

            LastBindedAddress = address;
            
            Host.Create(address,
                Configuration.PeerLimit,
                Configuration.ChannelLimit,
                Configuration.IncomingBandwidth,
                Configuration.OutgoingBandwidth);

            //Host.PreventConnections(true);
            //Host.EnableCompression();

            return NetDriverBindError.Success;
        }

        public void Listen()
        {
            //Host.PreventConnections(false);
        }

        public Peer Connect(IPEndPoint ipEndPoint)
        {
            return Connect(IPEndPointToENetAddress(ipEndPoint));
        }

        public Peer Connect(Address address)
        {
            //Host.PreventConnections(false);
            //Host.EnableCompression();
            
            return Host.Connect(address);
        }

        public Address IPEndPointToENetAddress(IPEndPoint ipEndPoint)
        {
            if (ipEndPoint == null)
                return default;
            
            var addr = new Address();
            var addrFailure = ipEndPoint.Address != null && !addr.SetHost(ipEndPoint.Address.ToString());
            addr.Port = (ushort) ipEndPoint.Port;
            
            if (addrFailure) Debug.LogError("addrFailure: " + ipEndPoint.Address.ToString());
            
            return addr;
        }
    }
}
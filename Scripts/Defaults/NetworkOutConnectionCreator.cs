using System.Net;
using LiteNetLib.Utils;

namespace package.stormiumteam.networking
{
    using LiteNetLib;

    public class NetworkOutConnectionCreator : IOutConnectionCreator, IConnection
    {
        public string ManagerAddress { get; set; }
        public short  ManagerPort    { get; set; }

        public NetManager Manager { get; set; }

        public string IpAddress;
        public int    Port;

        public NetPeer ConnectedPeer;

        public void Init()
        {
            
        }
        
        public void Execute(NetworkInstance emptyNetInstance)
        {
            
        }

        public IPEndPoint GetAddress()
        {
            return ConnectedPeer.EndPoint;
        }
    }
}
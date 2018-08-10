using System.Net;
using LiteNetLib.Utils;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    using LiteNetLib;

    public class NetworkSelfConnectionCreator : ISelfConnectionCreator, IConnectionHost
    {
        public string ManagerAddress { get; set; }
        public short  ManagerPort    { get; set; }

        public NetManager            Manager  { get; set; }
        public EventBasedNetListener Listener { get; set; }

        public void Execute(NetworkInstance emptyNetInstance)
        {
            emptyNetInstance.SetAsConnected();
        }

        public IPEndPoint GetAddress()
        {
            return new IPEndPoint(IPAddress.Parse("127.0.0.1"), Manager.LocalPort);
        }

        public void Init()
        {
            Listener = Listener ?? new EventBasedNetListener();
            Manager = Manager ?? new NetManager(Listener);
            Manager.Start(ManagerPort);
            
            Assert.IsFalse(Manager == null, "Manager == null");
            Assert.IsFalse(Listener == null, "Listener == null");
        }
    }
}
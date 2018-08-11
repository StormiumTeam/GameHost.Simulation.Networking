using System.Net;
using DefaultNamespace;
using LiteNetLib.Utils;
using package.stormiumteam.networking;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public static class NetworkConnectionCreator
    {
        private static void InternalCreateNetwork(NetworkSelfConnectionCreator connectionCreator,
                                                  out NetworkInstance          self)
        {
            connectionCreator.Init();
            
            self = new NetworkInstance(new NetworkConnectionInfo()
            {
                ConnectionType = ConnectionType.Self,
                Creator        = connectionCreator
            }, true, true);

            connectionCreator.Execute(self);
            
            Assert.IsTrue(connectionCreator.Manager.IsRunning, "connectionCreator.Manager.IsRunning");
        }
        
        public static void ConnectToNetwork(NetworkManager                  manager,
                                            NetDataWriter connectData,
                                            NetworkSelfConnectionCreator connectionCreator,
                                            IPEndPoint address,
                                            out NetworkInstance      self,
                                            out NetworkInstance @out)
        {
            InternalCreateNetwork(connectionCreator, out self);
            manager.AddInstance(self, ConnectionType.Self);

            var outPeer = connectionCreator.Manager.Connect(address, connectData ?? new NetDataWriter());
            if (outPeer != null)
            {
                var outCreator = new NetworkOutConnectionCreator();
                outCreator.Manager = connectionCreator.Manager;
                outCreator.IpAddress = outPeer.EndPoint.Address.ToString();
                outCreator.Port = outPeer.EndPoint.Port;
                outCreator.ConnectedPeer = outPeer;
                
                outCreator.Init();
                
                @out = new NetworkInstance(new NetworkConnectionInfo()
                {
                    Creator        = outCreator,
                    ConnectionType = ConnectionType.Out
                }, false, true);
                
                @out.SetMain(@out);
                self.SetMain(@out);

                outCreator.Execute(@out);

                manager.AddInstance(@out, ConnectionType.Out, self);
            }
            else
            {
                @out = null;
                
                Debug.LogWarning($"No connection could be made to: {address}");
            }
        }

        public static void CreateNetwork(NetworkManager                  manager,
                                         NetworkSelfConnectionCreator connectionCreator,
                                         out NetworkInstance self)
        {
            InternalCreateNetwork(connectionCreator, out self);
            self.SetMain(self);

            var userManager = self.GetUserManager();
            
            self.SetUser(userManager.Allocate(null));
            manager.AddInstance(self, ConnectionType.Self);
        }
    }
}
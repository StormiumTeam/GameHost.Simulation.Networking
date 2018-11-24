using Unity.Entities;

namespace package.stormiumteam.networking.Runtime.LowLevel
{
    public class NetworkInstanceSystem : ComponentSystem
    {
        /*public List<Host> ServerHosts;

        protected override void OnCreateManager()
        {
            ServerHosts = new List<Host>(2);
        }

        public NetworkServerHostInstance CreateHost()
        {
            var host = new Host();
            
            return new NetworkServerHostInstance(host);
        }

        public void DestroyHost(NetworkServerHostInstance networkServerHostInstance)
        {
            foreach (var peer in networkServerHostInstance.Peers)
            {
                peer.Value.DisconnectNow(0);
            }
            
            networkServerHostInstance.Host.Flush();
            networkServerHostInstance.Host.Dispose();
            networkServerHostInstance.Peers.Clear();
        }*/

        protected override void OnUpdate()
        {
            
        }
    }
}
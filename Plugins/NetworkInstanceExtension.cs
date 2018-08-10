using package.stormiumteam.networking;
using UnityEngine.Assertions;

namespace DefaultNamespace
{
    public static class NetworkInstanceExtension
    {
        public static ConnectionUserManager GetUserManager(this NetworkInstance t)
        {
            return t.World.GetOrCreateManager<ConnectionUserManager>();
        }
        
        public static ConnectionChannelManager GetChannelManager(this NetworkInstance t)
        {
            return t.World.GetOrCreateManager<ConnectionChannelManager>();
        }

        public static NetworkChannel GetDefaultChannel(this NetworkInstance t)
        {
            return GetChannelManager(t).DefaultChannel;
        }
        
        public static ConnectionMessageSystem GetMessageManager(this NetworkInstance t)
        {
            return t.World.GetOrCreateManager<ConnectionMessageSystem>();
        }
        
        public static ConnectionPatternManager GetPatternManager(this NetworkInstance t)
        {
            return t.World.GetOrCreateManager<ConnectionPatternManager>();
        }
        
        public static ConnectionEventManager GetEventManager(this NetworkInstance t)
        {
            return t.World.GetOrCreateManager<ConnectionEventManager>();
        }
        
        public static ConnectionUserManager GetUserManager(this NetPeerInstance t)
        {
            return t.Global.GetUserManager();
        }
        
        public static ConnectionChannelManager GetChannelManager(this NetPeerInstance t)
        {
            return t.Global.GetChannelManager();
        }
        
        public static ConnectionMessageSystem GetMessageManager(this NetPeerInstance t)
        {
            return t.Global.GetMessageManager();
        }
        
        public static ConnectionPatternManager GetPatternManager(this NetPeerInstance t)
        {
            return t.Global.GetPatternManager();
        }
        
        public static ConnectionEventManager GetEventManager(this NetPeerInstance t)
        {
            return t.Global.GetEventManager();
        }
    }
}
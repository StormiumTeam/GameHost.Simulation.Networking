using package.stormiumteam.shared;
using Scripts.Utilities;

namespace package.stormiumteam.networking
{
    public abstract class EventPeerInstanceClientReady
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetPeerInstance Instance;

            public Arguments(NetPeerInstance instance)
            {
                Instance = instance;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }
    
    public abstract class EventPeerInstanceServerReady
    {
        public struct Arguments : IDelayComponentArguments
        {
            public NetPeerInstance Instance;

            public Arguments(NetPeerInstance instance)
            {
                Instance = instance;
            }
        }

        public interface IEv : IAppEvent
        {
            void Callback(Arguments args);
        }

        internal abstract void Sealed();
    }
}
using ENet;
using package.stormiumteam.networking.Runtime.HighLevel;

namespace package.stormiumteam.networking.Runtime.LowLevel
{
    public struct NetworkConnection
    {
        public int ParentId;
        public int Id;

        public NetworkConnection(int id, int parentId = 0)
        {
            Id = id;
            ParentId = parentId;
        }

        public static NetworkConnection New(int parentId = 0)
        {
            return new NetworkConnection(s_Counter++, parentId);
        }

        private static int s_Counter = 1;
    }
}
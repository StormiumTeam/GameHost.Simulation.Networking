using System;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    [Flags]
    public enum SnapshotFlags
    {
        None = 0,
        FullData = 1,
        Local = 2,
        FullDataAndLocal = FullData | Local
    }
    
    public struct SnapshotReceiver
    {
        public Entity Client;
        public SnapshotFlags Flags;

        public SnapshotReceiver(Entity client, SnapshotFlags flags)
        {
            Client = client;
            Flags = flags;
        }
    }

    public struct SnapshotSender
    {
        public Entity Client;
        public SnapshotFlags Flags;

        public SnapshotSender(Entity client, SnapshotFlags flags)
        {
            Client = client;
            Flags = flags;
        }
    }
}
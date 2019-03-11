using package.stormiumteam.networking;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;

namespace StormiumShared.Core.Networking
{
    public interface ISnapshotEventObject : IAppEvent
    {
        PatternResult GetSystemPattern();
    }

    public interface ISnapshotSubscribe : ISnapshotEventObject
    {
        void SubscribeSystem();
    }

    public interface ISnapshotManageForClient : ISnapshotEventObject
    {
        DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime);
        void             ReadData(SnapshotSender    sender,   SnapshotRuntime runtime, DataBufferReader sysData);
    }
}
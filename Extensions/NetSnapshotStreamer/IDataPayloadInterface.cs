using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
	public interface IWriteEntityDataPayload
	{
		void Write(int index, Entity entity, DataBufferWriter data, SnapshotReceiver receiver, StSnapshotRuntime runtime);
	}

	public interface IReadEntityDataPayload
	{
		void Read(int index, Entity entity, ref DataBufferReader data, SnapshotSender sender, StSnapshotRuntime runtime);
	}

	public interface IMultiEntityDataPayload : IWriteEntityDataPayload, IReadEntityDataPayload
	{
	}
}
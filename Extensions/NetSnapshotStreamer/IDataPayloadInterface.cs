using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
	public interface IWriteEntityDataPayload<TState>
		where TState : struct, IComponentData
	{
		void Write(int index, Entity entity, ComponentDataFromEntity<TState> stateFromEntity, ComponentDataFromEntity<DataChanged<TState>> changeFromEntity, DataBufferWriter data, SnapshotReceiver receiver, StSnapshotRuntime runtime);
	}

	public interface IReadEntityDataPayload<TState>
		where TState : struct, IComponentData
	{
		void Read(int index, Entity entity, ComponentDataFromEntity<TState> dataFromEntity, ref DataBufferReader data, SnapshotSender sender, StSnapshotRuntime runtime);
	}

	public interface IMultiEntityDataPayload<TState> : IWriteEntityDataPayload<TState>, IReadEntityDataPayload<TState>
		where TState : struct, IComponentData
	{
	}

	public interface ISerializableAsPayload
	{
		void Write(ref DataBufferWriter data, SnapshotReceiver receiver, StSnapshotRuntime runtime);
		void Read(ref DataBufferReader data, SnapshotSender sender, StSnapshotRuntime runtime);
	}
}
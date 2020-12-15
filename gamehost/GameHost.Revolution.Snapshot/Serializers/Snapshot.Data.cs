using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Serializers
{
	// TODO: Move them correctly to apprioriate folders
	public interface ISnapshotData : IComponentBuffer
	{
		uint Tick { get; set; }
	}

	public interface ISnapshotSetupData
	{
		void Create(SerializerBase serializer);

		void Begin(bool isSerialization);
		void Clean();
	}

	public struct EmptySnapshotSetup : ISnapshotSetupData
	{
		public void Create(SerializerBase serializer)
		{
		}

		public void Begin(bool isSerialization)
		{
		}

		public void Clean()
		{
		}
	}
	
	public interface IReadWriteSnapshotData<T> : IReadWriteSnapshotData<T, EmptySnapshotSetup>
		where T : ISnapshotData
	{
	}

	public interface IReadWriteSnapshotData<T, TSetup> : ISnapshotData
		where T : ISnapshotData
		where TSetup : struct, ISnapshotSetupData
	{
		void Serialize(in   BitBuffer buffer, in T baseline, in TSetup setup);
		void Deserialize(in BitBuffer buffer, in T baseline, in TSetup setup);
	}

	public interface ISnapshotSyncWithComponent<TComponent> : ISnapshotSyncWithComponent<TComponent, EmptySnapshotSetup>
	{
	}

	public interface ISnapshotSyncWithComponent<TComponent, TSetup> : ISnapshotData
		where TSetup : struct, ISnapshotSetupData
	{
		void FromComponent(in TComponent component, in TSetup setup);
		void ToComponent(ref  TComponent component, in TSetup setup);
	}
}
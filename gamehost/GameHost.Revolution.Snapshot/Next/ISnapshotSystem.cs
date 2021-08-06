using DefaultEcs;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public interface ISnapshotSystem
	{
		/// <summary>
		/// Initialize data for the system
		/// </summary>
		/// <param name="writer">The writer that called this system</param>
		/// <param name="storage">The storage attached to this system</param>
		void PrepareWrite(SnapshotFrameWriter writer, Entity storage);

		/// <summary>
		/// Write data to a buffer, and use delta-encoding if <see cref="baselineStorage"/> isn't null
		/// </summary>
		/// <param name="buffer">Buffer to write on</param>
		/// <param name="storage">The current storage</param>
		/// <param name="baselineStorage">The storage to use as a delta base, can be null</param>
		void WriteBuffer(BitBuffer buffer, Entity storage, Entity? baselineStorage);

		/// <summary>
		/// Initialize data for the system. This method is always called
		/// </summary>
		/// <param name="writer">The writer that called this system</param>
		/// <param name="storage">The storage attached to this system</param>
		void PrepareRead(SnapshotFrameWriter writer, Entity storage);

		/// <summary>
		/// Read data from a buffer, and use delta-encoding if <see cref="baselineStorage"/> isn't null. This method may not be called.
		/// </summary>
		/// <param name="buffer">Buffer to read from</param>
		/// <param name="storage">The current storage</param>
		/// <param name="baselineStorage">The storage to use as a delta base, can be null</param>
		void ReadBuffer(BitBuffer buffer, Entity storage, Entity? baselineStorage);

		/// <summary>
		/// Complete read operation after <see cref="ReadBuffer"/> has been called. This method is always called.
		/// </summary>
		/// <param name="writer">The writer that called this system</param>
		/// <param name="storage">The storage attached to this system</param>
		void CompleteRead(SnapshotFrameWriter writer, Entity storage);
	}

	public interface ISnapshotSystemOnStorageDisposed
	{
		void OnStorageDisposed(Entity storage);
	}

	public interface ISnapshotSystemSupportArchetype
	{
		ISerializerArchetype GetSerializerArchetype();
	}
}
using System;
using System.Buffers;
using Collections.Pooled;
using GameHost.IO;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using StormiumTeam.GameBase.Utility.Misc.EntitySystem;

namespace GameHost.Revolution.NetCode.Next.Data
{
	/// <summary>
	/// This Manager only manage:
	/// - Archetypes Data
	/// - Entities Data
	/// - Systems Data
	///
	/// It does not manage:
	/// - Delta change
	/// - Authority/Ownership
	///
	/// It is a very simple manager as the name suggest,
	/// but is useful for very simple tasks that only need serializing. 
	/// </summary>
	/// <remarks>
	/// Such manager can be used in those contexts:
	/// - Resources synchronization
	/// - Static data
	/// - Coupled with a DeltaSnapshotManager it can be an excellent duo.
	/// (The Simple manager would manage resources and very static data, while the dynamic manager would send a lot of updates per second with more feature such as authority)
	/// </remarks>
	public partial class SimpleSnapshotManager
	{
		private BitBuffer buffer = new();

		private IBatchRunner runner;

		public SimpleSnapshotManager(IBatchRunner? runner = null)
		{
			this.runner = runner ?? new SingleThreadedBatchRunner();
		}

		public void Serialize(SnapshotFrame frame, PooledList<byte> bytes)
		{
			var request = runner.Queue(frame.GetSystemBatch());

			buffer.Clear();
			{
				Write.archetypes_data(buffer, frame);
				Write.entities(buffer, frame);
			}

			runner.WaitForCompletion(request);
			frame.CompleteSystemBatch(buffer);

			buffer.ToSpan(bytes.AddSpan(buffer.Length));
		}

		public void Deserialize(ref SnapshotFrame frame, Span<byte> bytes)
		{
			frame.Clear();

			buffer.Clear();
			buffer.AddSpan(bytes);
			{
				Reader.archetypes_data(buffer, frame);
				Reader.entities(buffer, frame);
			}

			frame.GetWriter().BuildInReadingContext();
			{
				// systems() should be the last called function since it read all the left data!
				Reader.systems(buffer, frame);
			}
		}
	}
}
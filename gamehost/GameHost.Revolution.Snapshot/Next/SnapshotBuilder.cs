using System;
using DefaultEcs;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public partial struct SnapshotBuilder : IDisposable
	{
		private SnapshotFrame baseline;
		private SnapshotFrame target;
		
		public partial void SetBaseline(SnapshotFrame snapshot)
		{
			baseline = snapshot;
		}

		private SnapshotFrameWriter writer;
		public partial void SetOutput(SnapshotFrame snapshot)
		{
			target = snapshot;
			writer = target.Write(this);
		}
		
		public partial void AddEntity(GameEntity entity)
		{
			writer.AddEntity(entity);
		}

		public partial void AddSystem(uint handle, ISnapshotSystem system)
		{
			writer.AddSystem(handle, system);
		}

		public void Dispose()
		{
		}

		public void Complete()
		{
			writer.Execute();
		}
	}
}
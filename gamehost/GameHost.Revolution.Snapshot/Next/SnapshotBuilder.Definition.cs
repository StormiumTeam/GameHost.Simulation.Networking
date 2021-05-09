using DefaultEcs;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public partial struct SnapshotBuilder
	{
		public partial void SetOutput(SnapshotFrame   snapshot);
		public partial void SetBaseline(SnapshotFrame snapshot);
		
		public partial void AddEntity(GameEntity entity);
		public partial void AddSystem(uint       handle, ISnapshotSystem system);
	}
}
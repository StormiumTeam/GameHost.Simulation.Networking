using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public readonly struct SnapshotEntity : IComponentData
	{
		public readonly GameEntity Source;
		public readonly int        Instigator;

		public SnapshotEntity(GameEntity source, int instigator)
		{
			Source     = source;
			Instigator = instigator;
		}

		public override string ToString()
		{
			return $"SnapshotEntity({Source.Id}, {Source.Version}; {Instigator})";
		}
	}
}
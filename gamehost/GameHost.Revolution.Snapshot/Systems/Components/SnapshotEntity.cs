using DefaultEcs;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public readonly struct SnapshotEntity : IComponentData
	{
		public struct CreatedByThisWorld : IComponentData
		{
		}

		public readonly struct ForcedInstigatorId : IComponentData
		{
			public readonly int Value;

			public ForcedInstigatorId(int value) => Value = value;
		}

		public readonly GameEntity Source;
		public readonly int        Instigator;
		public readonly Entity     Storage;

		public SnapshotEntity(GameEntity source, int instigator, Entity storage)
		{
			Source     = source;
			Instigator = instigator;
			Storage    = storage;
		}

		public override string ToString()
		{
			return $"SnapshotEntity({Source.Id}, {Source.Version}; {Instigator})";
		}
	}
}
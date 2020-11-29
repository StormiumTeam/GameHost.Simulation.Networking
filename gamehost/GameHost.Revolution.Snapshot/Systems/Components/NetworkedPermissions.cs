using DefaultEcs;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public struct IsComponentOwned<TComponent> : IComponentData
		where TComponent : IEntityComponent
	{
	}

	public struct SnapshotStorageOwned : IComponentData
	{
		public Entity Storage;

		/// <summary>
		///     This represent the owned archetype, that contains systems that the client is allowed to serialize.
		/// </summary>
		public uint OwnedArchetype;
	}
}
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public struct SnapshotOwnedWriteArchetype : IComponentData
	{
		/// <summary>
		///     This represent the owned archetype, that contains systems that the client is allowed to serialize.
		/// </summary>
		public uint OwnedArchetype;

		public SnapshotOwnedWriteArchetype(uint ownedArchetype)
		{
			OwnedArchetype = ownedArchetype;
		}
	}
}
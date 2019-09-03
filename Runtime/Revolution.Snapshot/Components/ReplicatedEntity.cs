using Unity.Entities;

namespace Revolution
{
	/// <summary>
	/// Represent an entity that was replicated from a snapshot
	/// </summary>
	public struct ReplicatedEntity : IComponentData
	{
		/// <summary>
		/// The ghost id of the entity
		/// </summary>
		public uint GhostId;
		/// <summary>
		/// The archetype of the entity
		/// </summary>
		public uint Archetype;
	}
}
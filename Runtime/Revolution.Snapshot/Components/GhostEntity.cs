using Unity.Entities;

namespace Revolution
{
	/// <summary>
	/// An entity that is replicated to clients
	/// </summary>
	public struct GhostEntity : IComponentData
	{
	}

	/// <summary>
	/// The ghost identifier of this entity. This component is automatically added.
	/// </summary>
	public struct GhostIdentifier : ISystemStateComponentData
	{
		public uint Value;
	}
}
using Unity.Entities;

namespace Revolution
{
	/// <summary>
	///     An entity that is replicated to clients
	/// </summary>
	public struct GhostEntity : IComponentData
	{
	}

	/// <summary>
	///     Indicate to the snapshot that this entity will be manually destroyed by another system
	/// </summary>
	public struct ManualDestroy : IComponentData
	{
	}

	/// <summary>
	///     Is this entity destroyed on the snapshot?
	/// </summary>
	public struct IsDestroyedOnSnapshot : IComponentData
	{
	}

	/// <summary>
	///     The ghost identifier of this entity. This component is automatically added.
	/// </summary>
	public struct GhostIdentifier : ISystemStateComponentData
	{
		public uint Value;

		public override string ToString()
		{
			return Value.ToString();
		}
	}

	public static class GhostIdentifierExtensions
	{
		public static uint GetGhost(this ComponentDataFromEntity<GhostIdentifier> ghostIdentifier, Entity entity)
		{
			if (ghostIdentifier.Exists(entity))
				return ghostIdentifier[entity].Value;
			return 0;
		}
	}
}
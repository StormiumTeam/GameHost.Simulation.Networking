using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;

namespace StormiumTeam.Networking.Utilities
{
	public struct GhostComponentType<T>
		where T : struct, IComponentData
	{
		public bool Equals(GhostComponentType<T> other)
		{
			return ComponentType.Equals(other.ComponentType) && Archetype.Equals(other.Archetype);
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			return obj is GhostComponentType<T> other && Equals(other);
		}

		public override int GetHashCode()
		{
			unchecked
			{
				return (ComponentType.GetHashCode() * 397) ^ Archetype.GetHashCode();
			}
		}

		public ComponentType ComponentType;

		[NativeDisableContainerSafetyRestriction]
		public ArchetypeChunkComponentType<T> Archetype;

		public static bool operator ==(ComponentType lhs, GhostComponentType<T> rhs)
		{
			return lhs == rhs.ComponentType;
		}

		public static bool operator !=(ComponentType lhs, GhostComponentType<T> rhs)
		{
			return lhs != rhs.ComponentType;
		}
	}

	public static class GhostComponentType
	{
		public static GhostComponentType<T> GetGhostComponentType<T>(this ComponentSystemBase componentSystem, bool readOnly = false)
			where T : struct, IComponentData
		{
			return new GhostComponentType<T>
			{
				ComponentType = readOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>(),
				Archetype     = componentSystem.GetArchetypeChunkComponentType<T>()
			};
		}

		public static void GetGhostComponentType<T>(this ComponentSystemBase componentSystem, out GhostComponentType<T> g, bool readOnly = false)
			where T : struct, IComponentData
		{
			g = new GhostComponentType<T>
			{
				ComponentType = readOnly ? ComponentType.ReadOnly<T>() : ComponentType.ReadWrite<T>(),
				Archetype     = componentSystem.GetArchetypeChunkComponentType<T>()
			};
		}
	}
}
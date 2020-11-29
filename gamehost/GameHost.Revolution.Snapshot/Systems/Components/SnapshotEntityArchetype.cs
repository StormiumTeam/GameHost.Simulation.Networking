using System;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public readonly struct SnapshotEntityArchetype : IEquatable<SnapshotEntityArchetype>
	{
		public readonly uint Id;

		public SnapshotEntityArchetype(uint id)
		{
			Id = id;
		}

		public bool Equals(SnapshotEntityArchetype other)
		{
			return Id == other.Id;
		}

		public override bool Equals(object? obj)
		{
			return obj is SnapshotEntityArchetype other && Equals(other);
		}

		public override int GetHashCode()
		{
			return (int) Id;
		}

		public static bool operator ==(SnapshotEntityArchetype left, SnapshotEntityArchetype right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(SnapshotEntityArchetype left, SnapshotEntityArchetype right)
		{
			return !left.Equals(right);
		}
	}
}
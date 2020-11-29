using System;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public readonly struct SnapshotSerializerSystem : IEquatable<SnapshotSerializerSystem>
	{
		public readonly uint Id;

		public SnapshotSerializerSystem(uint id)
		{
			Id = id;
		}

		public bool Equals(SnapshotSerializerSystem other)
		{
			return Id == other.Id;
		}

		public override bool Equals(object? obj)
		{
			return obj is SnapshotSerializerSystem other && Equals(other);
		}

		public override int GetHashCode()
		{
			return (int) Id;
		}

		public static bool operator ==(SnapshotSerializerSystem left, SnapshotSerializerSystem right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(SnapshotSerializerSystem left, SnapshotSerializerSystem right)
		{
			return !left.Equals(right);
		}
	}
}
using System;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public readonly struct InstigatorSystem : IEquatable<InstigatorSystem>
	{
		public readonly uint Id;

		public InstigatorSystem(uint id)
		{
			Id = id;
		}

		public bool Equals(InstigatorSystem other)
		{
			return Id == other.Id;
		}

		public override bool Equals(object? obj)
		{
			return obj is InstigatorSystem other && Equals(other);
		}

		public override int GetHashCode()
		{
			return (int) Id;
		}

		public static bool operator ==(InstigatorSystem left, InstigatorSystem right)
		{
			return left.Equals(right);
		}

		public static bool operator !=(InstigatorSystem left, InstigatorSystem right)
		{
			return !left.Equals(right);
		}
	}
}
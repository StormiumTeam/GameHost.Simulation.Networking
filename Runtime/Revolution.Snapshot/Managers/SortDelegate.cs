using System;
using Unity.Burst;

namespace Revolution
{
	public struct SortDelegate<T> : IComparable<SortDelegate<T>>
	{
		public FunctionPointer<T> Value;
		public int SystemId;

		public int CompareTo(SortDelegate<T> other)
		{
			return SystemId.CompareTo(other.SystemId);
		}

		public static bool operator <(SortDelegate<T> left, SortDelegate<T> right)
		{
			return left.CompareTo(right) < 0;
		}

		public static bool operator >(SortDelegate<T> left, SortDelegate<T> right)
		{
			return left.CompareTo(right) > 0;
		}

		public static bool operator <=(SortDelegate<T> left, SortDelegate<T> right)
		{
			return left.CompareTo(right) <= 0;
		}

		public static bool operator >=(SortDelegate<T> left, SortDelegate<T> right)
		{
			return left.CompareTo(right) >= 0;
		}
	}
}
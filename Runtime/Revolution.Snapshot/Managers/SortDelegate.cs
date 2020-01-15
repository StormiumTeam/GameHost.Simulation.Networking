using System;
using Unity.Burst;
using Unity.Collections;

namespace Revolution
{
	public struct SortDelegate<T> : IComparable<SortDelegate<T>>
	{
		public FunctionPointer<T> Value;
		public uint                SystemId;
		public NativeString512     Name;

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
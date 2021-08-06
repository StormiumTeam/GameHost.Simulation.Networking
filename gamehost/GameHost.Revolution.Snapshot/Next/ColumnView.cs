using System;
using System.Runtime.CompilerServices;

namespace GameHost.Revolution.NetCode.Next
{
	public class Column<TParent, T>
	{
		private ColumnView<TParent> parent;

		public T[] Array = System.Array.Empty<T>();

		public Column(ColumnView<TParent> parent)
		{
			this.parent = parent;
		}

		public ref T    GetValue(int index) => ref parent.GetColumn(index, ref Array);
		public ref T    GetValue(uint index) => ref parent.GetColumn(index, ref Array);

		public void Clear()
		{
			Array.AsSpan()
			     .Clear();
		}
	}
	
	public class ColumnView<T>
	{
		public T[] Array = System.Array.Empty<T>();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T GetValue(int index)
		{
			if (index >= Array.Length)
				System.Array.Resize(ref Array, index + 1);
			return ref Array[index];
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T GetValue(uint index)
		{
			if (index >= Array.Length)
				System.Array.Resize(ref Array, (int) index + 1);
			return ref Array[index];
		}

		public ref T2 GetColumn<T2>(int index, ref T2[] array)
		{
			if (Array.Length > array.Length)
				System.Array.Resize(ref array, Array.Length);
			return ref array[index];
		}

		public ref T2 GetColumn<T2>(uint index, ref T2[] array)
		{
			if (Array.Length > array.Length)
				System.Array.Resize(ref array, Array.Length);
			return ref array[index];
		}

		public static implicit operator Span<T>(ColumnView<T> columnView)
		{
			return columnView.Array.AsSpan();
		}

		public void Clear()
		{
			Array.AsSpan()
			     .Clear();
		}
	}
}
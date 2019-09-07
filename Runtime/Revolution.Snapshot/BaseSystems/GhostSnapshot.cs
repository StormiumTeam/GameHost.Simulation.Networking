using System;
using Collections.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Revolution
{
	public unsafe struct GhostSnapshotPointer
	{
		public uint Id;
	}

	public unsafe struct GhostSnapshot : IDisposable
	{
		public uint Id;

		public UnsafeHashMap* SystemData;

		public ref T TryGetSystemData<T>(uint systemId, out bool success)
			where T : struct
		{
			success = UnsafeHashMap.TryGetValue(SystemData, systemId, out IntPtr ptr);
			return ref UnsafeUtilityEx.AsRef<T>(ptr.ToPointer());
		}

		public ref T AllocateSystemData<T>(uint systemId)
			where T : struct
		{
			var ptr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
			UnsafeHashMap.Add(SystemData, systemId, (IntPtr) ptr);
			return ref UnsafeUtilityEx.AsRef<T>(ptr);
		}

		public void DeallocateSystemData(uint systemId)
		{
			if (!UnsafeHashMap.ContainsKey(SystemData, systemId))
				throw new InvalidOperationException();
			UnsafeUtility.Free((void*) UnsafeHashMap.Get<uint, IntPtr>(SystemData, systemId), Allocator.Persistent);
			UnsafeHashMap.Remove(SystemData, systemId);
		}

		public void Dispose()
		{
			foreach (var val in UnsafeHashMap.GetIterator<uint, IntPtr>(SystemData))
			{
				UnsafeUtility.Free((void*) val.value, Allocator.Persistent);
			}

			UnsafeHashMap.Free(SystemData);
		}
	}
}
//#define DICTIONARY

using System;
using System.Runtime.CompilerServices;
using Collections.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Revolution
{
	public struct GhostSnapshotPointer
	{
		public uint Id;
	}

	public unsafe struct GhostSnapshot : IDisposable
	{
		public uint Id;

		
#if DICTIONARY
		public UnsafeHashMap* SystemData;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T TryGetSystemData<T>(uint systemId, out bool success)
			where T : struct
		{
			success = UnsafeHashMap.TryGetValue(SystemData, systemId, out IntPtr ptr);
			return ref Unsafe.AsRef<T>(ptr.ToPointer());
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T AllocateSystemData<T>(uint systemId)
			where T : struct
		{
			var ptr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
			UnsafeHashMap.Add(SystemData, systemId, (IntPtr) ptr);
			return ref UnsafeUtilityEx.AsRef<T>(ptr);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void DeallocateSystemData(uint systemId)
		{
			if (!UnsafeHashMap.ContainsKey(SystemData, systemId))
				throw new InvalidOperationException();
			UnsafeUtility.Free((void*) UnsafeHashMap.Get<uint, IntPtr>(SystemData, systemId), Allocator.Persistent);
			UnsafeHashMap.Remove(SystemData, systemId);
		}

		public void Dispose()
		{
			foreach (var val in UnsafeHashMap.GetIterator<uint, IntPtr>(SystemData)) UnsafeUtility.Free((void*) val.value, Allocator.Persistent);

			UnsafeHashMap.Free(SystemData);
		}
		
		public void Allocate()
		{
			SystemData = UnsafeHashMap.Allocate<uint, IntPtr>(256);
		}
#else
		public UnsafePtrList* SystemData;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T TryGetSystemData<T>(uint systemId, out bool success)
			where T : struct
		{
			var sd = SystemData->Ptr[systemId];
			success = sd != null;
			return ref UnsafeUtility.AsRef<T>(sd);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public ref T AllocateSystemData<T>(uint systemId)
			where T : struct
		{
			var ptr = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator.Persistent);
			if (SystemData->Length <= systemId)
			{
				SystemData->Resize((int) systemId + 1, NativeArrayOptions.ClearMemory);
			}

			SystemData->Ptr[systemId] = ptr;
			return ref UnsafeUtility.AsRef<T>(ptr);
		}

		public void Allocate()
		{
			SystemData = UnsafePtrList.Create(256, Allocator.Persistent, NativeArrayOptions.ClearMemory);
		}

		public void Dispose()
		{
			for (var i = 0; i != SystemData->Length; i++)
			{
				UnsafeUtility.Free(SystemData->Ptr[i], Allocator.Persistent);
			}

			UnsafePtrList.Destroy(SystemData);
		}
#endif
	}
}
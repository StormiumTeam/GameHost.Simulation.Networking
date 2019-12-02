using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Revolution
{
	public static unsafe class SafetyUtility
	{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
		public static void Replace<T>(ref BufferFromEntity<T> bfe, AtomicSafetyHandle safetyHandle)
			where T : struct, IBufferElementData
		{
			// remove safety... (the array goes only writeonly for some weird reasons)
			UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref bfe),
				UnsafeUtility.AddressOf(ref safetyHandle),
				sizeof(AtomicSafetyHandle));

			UnsafeUtility.MemCpy((byte*) UnsafeUtility.AddressOf(ref bfe) + sizeof(AtomicSafetyHandle),
				UnsafeUtility.AddressOf(ref safetyHandle),
				sizeof(AtomicSafetyHandle));

		}

		public static void Replace<T>(ref ComponentDataFromEntity<T> bfe, AtomicSafetyHandle safetyHandle)
			where T : struct, IComponentData
		{
			// remove safety... (the array goes only writeonly for some weird reasons)
			UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref bfe),
				UnsafeUtility.AddressOf(ref safetyHandle),
				sizeof(AtomicSafetyHandle));
		}

		private static int m_ArchetypeComponentSafetyVarPos = -1;

		public static void Replace<T>(ref ArchetypeChunkComponentType<T> bfe, AtomicSafetyHandle safetyHandle)
			where T : struct, IComponentData
		{
			if (m_ArchetypeComponentSafetyVarPos < 0)
			{
				Init();
			}

			// remove safety... (the array goes only writeonly for some weird reasons)
			UnsafeUtility.MemCpy((byte*) UnsafeUtility.AddressOf(ref bfe) + m_ArchetypeComponentSafetyVarPos,
				UnsafeUtility.AddressOf(ref safetyHandle),
				sizeof(AtomicSafetyHandle));
		}

		private static void Init()
		{
			var type = typeof(ArchetypeChunkComponentType<>);
			var field = type.GetField("m_Safety", BindingFlags.NonPublic |
			                                      BindingFlags.Instance |
			                                      BindingFlags.GetField);

			m_ArchetypeComponentSafetyVarPos = UnsafeUtility.GetFieldOffset(field);
		}
#endif
	}
}
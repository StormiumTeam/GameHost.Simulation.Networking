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
#endif
		}
	}
}
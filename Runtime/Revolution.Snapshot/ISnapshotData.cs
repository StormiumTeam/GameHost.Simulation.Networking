using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport.Utilities;

namespace Revolution
{
	/// <summary>
	///     A typical snapshot buffer
	/// </summary>
	/// <typeparam name="T">Snapshot Type</typeparam>
	public interface ISnapshotData<T> : IBufferElementData
		where T : struct, ISnapshotData<T>
	{
		uint Tick { get; set; }
	}

	/// <summary>
	///     Represent a data that can be interpolated with another of the same type
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public interface IInterpolatable<T>
		where T : struct
	{
		void Interpolate(T target, float factor);
	}

	public static unsafe class SnapshotDataExtensions
	{
		public static ref TSnapshot GetLastBaseline<TSnapshot>(this DynamicBuffer<TSnapshot> snapshotBuffer)
			where TSnapshot : struct, ISnapshotData<TSnapshot>
		{
			if (snapshotBuffer.Length == 0)
				snapshotBuffer.Add(default); // needed or else we will read out of range on first execution

			var ptr = snapshotBuffer.GetUnsafePtr();
			return ref UnsafeUtilityEx.ArrayElementAsRef<TSnapshot>(ptr, snapshotBuffer.Length - 1);
		}

		public static TSnapshot GetLastBaselineReadOnly<TSnapshot>(this DynamicBuffer<TSnapshot> snapshotBuffer)
			where TSnapshot : struct, ISnapshotData<TSnapshot>
		{
			if (snapshotBuffer.Length == 0)
				snapshotBuffer.Add(default); // needed or else we will read out of range on first execution

			var ptr = snapshotBuffer.AsNativeArray().GetUnsafeReadOnlyPtr();
			return UnsafeUtility.ReadArrayElement<TSnapshot>(ptr, snapshotBuffer.Length - 1);
		}

        public static bool GetDataAtTick<T>(this DynamicBuffer<T> snapshotArray, uint targetTick, out T snapshotData)
            where T : struct, ISnapshotData<T>, IInterpolatable<T>
        {
            return GetDataAtTick(snapshotArray, targetTick, 1, out snapshotData);
        }

        public static bool GetDataAtTick<T>(this DynamicBuffer<T> snapshotArray, uint targetTick,
            float targetTickFraction, out T snapshotData) where T : struct, ISnapshotData<T>, IInterpolatable<T>
        {
	        int  beforeIdx  = 0;
	        uint beforeTick = 0;
	        int  afterIdx   = 0;
	        uint afterTick  = 0;
	        // If last tick is fractional before should not include the tick we are targeting, it should instead be included in after
	        if (targetTickFraction < 1)
		        --targetTick;
	        for (int i = 0; i < snapshotArray.Length; ++i)
	        {
		        uint tick = snapshotArray[i].Tick;
		        if (!SequenceHelpers.IsNewer(tick, targetTick) &&
		            (beforeTick == 0 || SequenceHelpers.IsNewer(tick, beforeTick)))
		        {
			        beforeIdx  = i;
			        beforeTick = tick;
		        }

		        if (SequenceHelpers.IsNewer(tick, targetTick) &&
		            (afterTick == 0 || SequenceHelpers.IsNewer(afterTick, tick)))
		        {
			        afterIdx  = i;
			        afterTick = tick;
		        }
	        }

	        if (beforeTick == 0)
	        {
		        snapshotData = default(T);
		        return false;
	        }

	        snapshotData = snapshotArray[beforeIdx];
	        if (afterTick == 0)
		        return true;
	        var   after       = snapshotArray[afterIdx];
	        float afterWeight = (float) (targetTick - beforeTick) / (float) (afterTick - beforeTick);
	        if (targetTickFraction < 1)
		        afterWeight += targetTickFraction / (float) (afterTick - beforeTick);
	        snapshotData.Interpolate(after, afterWeight);
            return true;
        }
	}
}
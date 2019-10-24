using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport.Utilities;

namespace Revolution
{
	/// <summary>
	/// A typical snapshot buffer
	/// </summary>
	/// <typeparam name="T">Snapshot Type</typeparam>
	public interface ISnapshotData<T> : IBufferElementData
		where T : struct, ISnapshotData<T>
	{
		uint Tick { get; set; }
	}

	/// <summary>
	/// Represent a data that can be interpolated with another of the same type
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
				snapshotBuffer.Add(default(TSnapshot)); // needed or else we will read out of range on first execution

			var ptr = snapshotBuffer.GetUnsafePtr();
			return ref UnsafeUtilityEx.ArrayElementAsRef<TSnapshot>(ptr, snapshotBuffer.Length - 1);
		}

		public static TSnapshot GetLastBaselineReadOnly<TSnapshot>(this DynamicBuffer<TSnapshot> snapshotBuffer)
			where TSnapshot : struct, ISnapshotData<TSnapshot>
		{
			if (snapshotBuffer.Length == 0)
				snapshotBuffer.Add(default(TSnapshot)); // needed or else we will read out of range on first execution

			var ptr = snapshotBuffer.AsNativeArray().GetUnsafeReadOnlyPtr();
			return UnsafeUtility.ReadArrayElement<TSnapshot>(ptr, snapshotBuffer.Length - 1);
		}

		public static bool GetDataAtTick<TSnapshot>(this DynamicBuffer<TSnapshot> snapshotBuffer, uint targetTick, out TSnapshot snapshotData)
			where TSnapshot : struct, ISnapshotData<TSnapshot>, IInterpolatable<TSnapshot>
		{
			if (snapshotBuffer.Length > 0 && GetLastBaselineReadOnly(snapshotBuffer).Tick == 0)
			{
				snapshotData = GetLastBaselineReadOnly(snapshotBuffer);
				return true;
			}

			int  beforeIdx  = 0;
			uint beforeTick = 0;
			int  afterIdx   = 0;
			uint afterTick  = 0;
			for (int i = 0; i < snapshotBuffer.Length; ++i)
			{
				uint tick = snapshotBuffer[i].Tick;
				if (!SequenceHelpers.IsNewer(tick, targetTick) && (beforeTick == 0 || SequenceHelpers.IsNewer(tick, beforeTick)))
				{
					beforeIdx  = i;
					beforeTick = tick;
				}

				if (SequenceHelpers.IsNewer(tick, targetTick) && (afterTick == 0 || SequenceHelpers.IsNewer(afterTick, tick)))
				{
					afterIdx  = i;
					afterTick = tick;
				}
			}

			if (beforeTick == 0)
			{
				snapshotData = default(TSnapshot);
				return false;
			}

			snapshotData = snapshotBuffer[beforeIdx];
			if (afterTick == 0)
				return true;
			var   after       = snapshotBuffer[afterIdx];
			float afterWeight = (float) (targetTick - beforeTick) / (float) (afterTick - beforeTick);
			snapshotData.Interpolate(after, afterWeight);
			return true;
		}
	}
}
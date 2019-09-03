using System;
using Revolution;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution
{
	[Flags]
	public enum DeltaChangeType
	{
		Chunk     = 1,
		Component = 2,
		Both      = 3
	}

	public interface ISnapshotDelta<in TSnapshot>
		where TSnapshot : ISnapshotDelta<TSnapshot>
	{
		bool DidChange(TSnapshot baseline);
	}

	public abstract class ComponentSnapshotSystem_Delta<TComponent, TSnapshot> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		ComponentSnapshotSystem_Delta<TComponent, TSnapshot>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent>, IRwSnapshotComplement<TSnapshot>, ISnapshotDelta<TSnapshot>
		where TComponent : struct, IComponentData
	{
		public struct SharedData
		{
			public uint            SystemVersion;
			public DeltaChangeType Delta;

			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public BufferFromEntity<TSnapshot>             SnapshotFromEntity;
		}

		[BurstCompile]
		public static void Serialize(uint systemId, ref SerializeClientData jobData, ref DataStreamWriter writer)
		{
			var sharedData = GetShared();
			var chunks     = GetSerializerChunkData().Array;

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;
			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(jobData.GhostType);

				var shouldSkip = false;
				if (deltaOnChunk)
				{
					shouldSkip = !chunk.DidChange(sharedData.ComponentTypeArch, sharedData.SystemVersion);
					writer.WritePackedUInt(shouldSkip ? 1u : 0u, jobData.NetworkCompressionModel);
					writer.WritePackedUIntDelta((uint) chunk.Count, previousChunkCount, jobData.NetworkCompressionModel);

					previousChunkCount = (uint) chunk.Count;
				}

				if (shouldSkip)
					continue;

				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!jobData.TryGetSnapshot(ghostArray[ent].Value, out var ghostSnapshot))
					{
						throw new InvalidOperationException("A ghost should have a snapshot.");
					}

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TSnapshot>(systemId, out var success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TSnapshot>(systemId);
						baseline = default; // always set to default values!
					}

					var newSnapshot = default(TSnapshot);
					newSnapshot.SynchronizeFrom(componentArray[ent]);

					// If we must check for delta change on components and
					// If the snapshot didn't changed since the previous baseline and
					// If the ghost using the snapshot isn't new then we can skip the serialize operation.
					if (deltaOnComponent)
					{
						// no change? skip
						if (!newSnapshot.DidChange(baseline) && success)
						{
							writer.WritePackedUInt(1, jobData.NetworkCompressionModel);
							continue;
						}

						// don't skip
						writer.WritePackedUInt(0, jobData.NetworkCompressionModel);
					}

					newSnapshot.WriteTo(writer, ref baseline, jobData.NetworkCompressionModel);

					baseline = newSnapshot;
				}
			}
		}

		[BurstCompile]
		public static void Deserialize(uint systemId, uint tick, ref DeserializeClientData jobData, ref DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var sharedData = GetShared();
			var ghostArray = GetDeserializerGhostData().Array;

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;
			var checkChunkSkipIn = 0;
			for (int ent = 0, length = ghostArray.Length; ent < length; ent++, checkChunkSkipIn--)
			{
				bool shouldSkip;
				if (deltaOnChunk && checkChunkSkipIn <= 0)
				{
					shouldSkip         = reader.ReadPackedUInt(ref ctx, jobData.NetworkCompressionModel) == 1;
					previousChunkCount = reader.ReadPackedUIntDelta(ref ctx, previousChunkCount, jobData.NetworkCompressionModel);
					
					if (shouldSkip)
					{
						ent += (int) previousChunkCount;
						continue;
					}

					checkChunkSkipIn = (int) previousChunkCount;
				}

				if (deltaOnComponent)
				{
					shouldSkip = reader.ReadPackedUInt(ref ctx, jobData.NetworkCompressionModel) == 1;
					
					if (shouldSkip)
						continue;
				}

				var     snapshotArray = sharedData.SnapshotFromEntity[jobData.GhostToEntityMap[ghostArray[ent]]];
				ref var baseline      = ref snapshotArray.GetLastBaseline();

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TSnapshot);
				newSnapshot.Tick = tick;
				newSnapshot.ReadFrom(ref ctx, reader, ref baseline, jobData.NetworkCompressionModel);

				snapshotArray.Add(newSnapshot);
			}
		}

		public virtual DeltaChangeType DeltaType => DeltaChangeType.Both;

		internal override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		internal override void SystemBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.Delta             = DeltaType;
			sharedData.SystemVersion     = GlobalSystemVersion - 1;
			sharedData.ComponentTypeArch = GetArchetypeChunkComponentType<TComponent>(true);
		}

		internal override void SystemBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetBufferFromEntity<TSnapshot>();
			SetEmptySafetyHandle(ref snapshotBuffer);

			ref var sharedData = ref GetShared();
			sharedData.Delta              = DeltaType;
			sharedData.SnapshotFromEntity = snapshotBuffer;
		}
	}
}
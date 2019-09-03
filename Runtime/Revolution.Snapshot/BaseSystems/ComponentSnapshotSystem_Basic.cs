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
	public abstract class ComponentSnapshotSystem_Basic<TComponent, TSnapshot> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		ComponentSnapshotSystem_Basic<TComponent, TSnapshot>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent>, IRwSnapshotComplement<TSnapshot>
		where TComponent : struct, IComponentData
	{
		public struct SharedData
		{
			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public BufferFromEntity<TSnapshot>             SnapshotFromEntity;
		}

		[BurstCompile]
		public static void Serialize(uint systemId, ref SerializeClientData jobData, ref DataStreamWriter writer)
		{
			var sharedData = GetShared();
			var chunks     = GetSerializerChunkData().Array;

			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(jobData.GhostType);
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

			for (int ent = 0, length = ghostArray.Length; ent < length; ent++)
			{
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

		internal override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		internal override void SystemBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.ComponentTypeArch = GetArchetypeChunkComponentType<TComponent>();
		}

		internal override void SystemBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetBufferFromEntity<TSnapshot>();
			SetEmptySafetyHandle(ref snapshotBuffer);

			ref var sharedData = ref GetShared();
			sharedData.SnapshotFromEntity = snapshotBuffer;
		}
	}
}
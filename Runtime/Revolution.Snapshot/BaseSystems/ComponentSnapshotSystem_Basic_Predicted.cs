using System;
using Unity.Burst;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{
	[BurstCompile]
	public abstract class ComponentSnapshotSystem_Basic_Predicted<TComponent, TSnapshot, TSetup> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		TSetup,
		ComponentSnapshotSystem_Basic_Predicted<TComponent, TSnapshot, TSetup>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IRwSnapshotComplement<TSnapshot>, IPredictable<TSnapshot>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISetup
	{
		public struct SharedData
		{
			public TSetup                                  SetupData;
			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public BufferFromEntity<TSnapshot>             SnapshotFromEntity;
		}

		public struct TripleBaseline
		{
			public uint Available;
			public TSnapshot Baseline0;
			public TSnapshot Baseline1;
			public TSnapshot Baseline2;
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

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TripleBaseline>(systemId, out var success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TripleBaseline>(systemId);
						baseline = default; // always set to default values!
					}

					var newSnapshot = default(TSnapshot);
					newSnapshot.Tick = jobData.Tick;
					newSnapshot.SynchronizeFrom(componentArray[ent], sharedData.SetupData, in jobData);
					if (baseline.Available >= 3)
					{
						baseline.Available = 3;
						baseline.Baseline0.PredictDelta(jobData.Tick, ref baseline.Baseline1, ref baseline.Baseline2);
					}

					newSnapshot.WriteTo(writer, ref baseline.Baseline0, jobData.NetworkCompressionModel);

					baseline.Baseline2 = baseline.Baseline1;
					baseline.Baseline1 = baseline.Baseline0;
					baseline.Baseline0 = newSnapshot;
					baseline.Available++;
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
				var     baseline2     = baseline;
				var     baseline3     = baseline;
				var     available     = 0;
				for (var i = 0; i != snapshotArray.Length; i++)
				{
					if (snapshotArray[i].Tick == tick - 2)
					{
						baseline2 = snapshotArray[i];
						available++;
					}

					if (snapshotArray[i].Tick == tick - 3)
					{
						baseline3 = snapshotArray[i];
						available++;
					}
				}

				if (available == 2 && baseline.Tick > 0)
				{
					baseline.PredictDelta(tick, ref baseline2, ref baseline3);
				}

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TSnapshot);
				newSnapshot.Tick = tick;
				newSnapshot.ReadFrom(ref ctx, reader, ref baseline, jobData.NetworkCompressionModel);

				snapshotArray.Add(newSnapshot);
			}
		}

		protected override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		internal override void SystemBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.ComponentTypeArch = GetArchetypeChunkComponentType<TComponent>(true);
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
			
			SetEmptySafetyHandle(ref sharedData.ComponentTypeArch);
		}

		internal override void SystemBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetBufferFromEntity<TSnapshot>();
			SetEmptySafetyHandle(ref snapshotBuffer);

			ref var sharedData = ref GetShared();
			sharedData.SnapshotFromEntity = snapshotBuffer;
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
		}
	}

	public abstract class ComponentSnapshotSystem_Basic_Predicted<TComponent, TSnapshot> : ComponentSnapshotSystem_Basic_Predicted
	<
		TComponent,
		TSnapshot,
		DefaultSetup
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>, IRwSnapshotComplement<TSnapshot>, IPredictable<TSnapshot>
		where TComponent : struct, IComponentData
	{
	}
}
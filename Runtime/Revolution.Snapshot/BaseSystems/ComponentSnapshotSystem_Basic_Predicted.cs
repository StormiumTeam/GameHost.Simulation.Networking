using System;
using Unity.Burst;
using Unity.Entities;

namespace Revolution
{
	[BurstCompile]
	public abstract class ComponentSnapshotSystemBasicPredicted<TComponent, TSnapshot, TSetup> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		TSetup,
		ComponentSnapshotSystemBasicPredicted<TComponent, TSnapshot, TSetup>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IRwSnapshotComplement<TSnapshot>, IPredictable<TSnapshot>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISetup
	{
		public override string ToString()
		{
			return $"ComponentSnapshotSystemBasicPredicted<{typeof(TComponent)}>";
		}
		
		[BurstCompile]
		public static void Serialize(ref SerializeParameters parameters)
		{
			var sharedData = GetShared();
			
			ref var clientData = ref parameters.GetClientData();
			ref var stream     = ref parameters.GetStream();

			var tick   = clientData.Tick;
			var chunks = parameters.ChunksToSerialize;

			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(clientData.GhostType);
				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!clientData.TryGetSnapshot(ghostArray[ent].Value, out var ghostSnapshot)) throw new InvalidOperationException("A ghost should have a snapshot.");

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TripleBaseline>(parameters.SystemId, out var success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TripleBaseline>(parameters.SystemId);
						baseline = default; // always set to default values!
					}

					var newSnapshot = default(TSnapshot);
					newSnapshot.Tick = tick;
					newSnapshot.SynchronizeFrom(componentArray[ent], sharedData.SetupData, in clientData);
					if (baseline.Available >= 3)
					{
						baseline.Available = 3;
						baseline.Baseline0.PredictDelta(tick, ref baseline.Baseline1, ref baseline.Baseline2);
					}

					newSnapshot.WriteTo(stream, ref baseline.Baseline0, clientData.NetworkCompressionModel);

					baseline.Baseline2 = baseline.Baseline1;
					baseline.Baseline1 = baseline.Baseline0;
					baseline.Baseline0 = newSnapshot;
					baseline.Available++;
				}
			}
		}

		[BurstCompile]
		public static void Deserialize(ref DeserializeParameters parameters)
		{
			var sharedData = GetShared();
			
			var ghostArray = parameters.GhostsToDeserialize;

			var clientData = parameters.GetClientData();
			var tick = clientData.Tick;

			for (int ent = 0, length = ghostArray.Length; ent < length; ent++)
			{
				var     snapshotArray = sharedData.SnapshotFromEntity[clientData.GhostToEntityMap[ghostArray[ent]]];
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

				if (available == 2 && baseline.Tick > 0) baseline.PredictDelta(tick, ref baseline2, ref baseline3);

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TSnapshot);
				newSnapshot.Tick = tick;
				newSnapshot.ReadFrom(ref parameters.Ctx, parameters.Stream, ref baseline, clientData.NetworkCompressionModel);

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

		public struct SharedData
		{
			public TSetup                                  SetupData;
			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public BufferFromEntity<TSnapshot>             SnapshotFromEntity;
		}

		public struct TripleBaseline
		{
			public uint      Available;
			public TSnapshot Baseline0;
			public TSnapshot Baseline1;
			public TSnapshot Baseline2;
		}
	}

	public abstract class ComponentSnapshotSystemBasicPredicted<TComponent, TSnapshot> : ComponentSnapshotSystemBasicPredicted
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
using System;
using Unity.Burst;
using Unity.Entities;

namespace Revolution
{
	[BurstCompile]
	public abstract class ComponentSnapshotSystemBasic<TComponent, TSnapshot, TSetup> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		TSetup,
		ComponentSnapshotSystemBasic<TComponent, TSnapshot, TSetup>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IRwSnapshotComplement<TSnapshot>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISetup
	{
		public override string ToString()
		{
			return $"ComponentSnapshotSystemBasic<{typeof(TComponent)}>";
		}
		
		[BurstCompile]
		public static void Serialize(ref SerializeParameters parameters)
		{
			var sharedData = GetShared();
			
			ref var clientData = ref parameters.GetClientData();
			ref var stream = ref parameters.GetStream();

			var tick = clientData.Tick;
			var chunks = parameters.ChunksToSerialize;

			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(clientData.GhostType);

				bool          success;
				GhostSnapshot ghostSnapshot;
				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!clientData.TryGetSnapshot(ghostArray[ent].Value, out ghostSnapshot)) throw new InvalidOperationException("A ghost should have a snapshot.");

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TSnapshot>(parameters.SystemId, out success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TSnapshot>(parameters.SystemId);
						baseline = default; // always set to default values!
					}

					var newSnapshot = default(TSnapshot);
					newSnapshot.Tick = tick;
					newSnapshot.SynchronizeFrom(componentArray[ent], sharedData.SetupData, in clientData);
					newSnapshot.WriteTo(stream, ref baseline, clientData.NetworkCompressionModel);

					baseline = newSnapshot;
				}
			}
		}

		[BurstCompile]
		public static void Deserialize(ref DeserializeParameters parameters)
		{
			var sharedData = GetShared();
			var ghostArray = parameters.GhostsToDeserialize;
			var clientData = parameters.GetClientData();

			for (int ent = 0, length = ghostArray.Length; ent < length; ent++)
			{
				var     snapshotArray = sharedData.SnapshotFromEntity[clientData.GhostToEntityMap[ghostArray[ent]]];
				ref var baseline      = ref snapshotArray.GetLastBaseline();

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TSnapshot);
				newSnapshot.Tick = clientData.Tick;
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
	}

	public abstract class ComponentSnapshotSystemBasic<TComponent, TSnapshot> : ComponentSnapshotSystemBasic
	<
		TComponent,
		TSnapshot,
		DefaultSetup
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>, IRwSnapshotComplement<TSnapshot>
		where TComponent : struct, IComponentData
	{
	}
}
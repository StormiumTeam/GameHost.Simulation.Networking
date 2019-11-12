using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{
	public interface IReadWriteComponentSnapshot<T, TSetup> : IComponentData
		where T : struct, IReadWriteComponentSnapshot<T, TSetup>
	{
		void WriteTo(DataStreamWriter              writer, ref T            baseline, TSetup setup,    SerializeClientData   jobData);
		void ReadFrom(ref DataStreamReader.Context ctx,    DataStreamReader reader,   ref T  baseline, DeserializeClientData jobData);
	}

	public interface IReadWriteComponentSnapshot<T> : IReadWriteComponentSnapshot<T, DefaultSetup>
		where T : struct, IReadWriteComponentSnapshot<T, DefaultSetup>
	{
	}

	public abstract class MixedComponentSnapshotSystem<TComponent, TSetup> : EntitySerializerComponent<MixedComponentSnapshotSystem<TComponent, TSetup>,
		TComponent,
		MixedComponentSnapshotSystem<TComponent, TSetup>.SharedData>
		where TComponent : struct, IComponentData, IReadWriteComponentSnapshot<TComponent, TSetup>
		where TSetup : struct, ISetup
	{
		public struct SharedData
		{
			public TSetup                                  SetupData;
			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public ComponentDataFromEntity<TComponent>     ComponentFromEntity;
		}

		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp)
			{
				[0] = typeof(TComponent)
			};

		[BurstCompile]
		public static void Serialize(uint systemId, ref SerializeClientData jobData, ref DataStreamWriter writer)
		{
			var sharedData = GetShared();
			var chunks     = GetSerializerChunkData().Array;
			
			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray = chunk.GetNativeArray(jobData.GhostType);

				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!jobData.TryGetSnapshot(ghostArray[ent].Value, out var ghostSnapshot))
					{
						throw new InvalidOperationException("A ghost should have a snapshot.");
					}

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TComponent>(systemId, out var success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TComponent>(systemId);
						baseline = default; // always set to default values!
					}
		
					componentArray[ent].WriteTo(writer, ref baseline, sharedData.SetupData, jobData);
					baseline = componentArray[ent];
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
				var entity      = jobData.GhostToEntityMap[ghostArray[ent]];
				var baseline    = sharedData.ComponentFromEntity[entity];
				var newSnapshot = default(TComponent);
				
				newSnapshot.ReadFrom(ref ctx, reader, ref baseline, jobData);

				sharedData.ComponentFromEntity[entity] = newSnapshot;
			}
		}

		protected override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		public override void OnBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.ComponentTypeArch = GetArchetypeChunkComponentType<TComponent>();
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
		}

		public override void OnBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetComponentDataFromEntity<TComponent>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyUtility.Replace(ref snapshotBuffer, SafetyHandle);
#endif

			ref var sharedData = ref GetShared();
			sharedData.ComponentFromEntity = snapshotBuffer;
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
		}
	}
}
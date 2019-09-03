using Revolution;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Transforms;

namespace DefaultNamespace
{
	// --
	// Represent an archetype that have a Translation and Health component.
	// Right now, you need to add a lot of boilerplate to make it work
	// In the future, it will be easier to define archetypes.
	// Also, keep in mind this archetype can have components removed and added (eg: adding a Team component)
	
	public struct CustomArchetypeTag : IComponentData
	{
	}

	public struct TestCustomArchetypeSnapshot : IReadWriteSnapshot<TestCustomArchetypeSnapshot>
	{
		public TranslationSnapshot TranslationSnapshot;
		public Health.Snapshot     HealthSnapshot;

		public void WriteTo(DataStreamWriter writer, ref TestCustomArchetypeSnapshot baseline, NetworkCompressionModel compressionModel)
		{
			TranslationSnapshot.WriteTo(writer, ref baseline.TranslationSnapshot, compressionModel);
			HealthSnapshot.WriteTo(writer, ref baseline.HealthSnapshot, compressionModel);
		}

		public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref TestCustomArchetypeSnapshot baseline, NetworkCompressionModel compressionModel)
		{
			TranslationSnapshot.ReadFrom(ref ctx, reader, ref baseline.TranslationSnapshot, compressionModel);
			HealthSnapshot.ReadFrom(ref ctx, reader, ref baseline.HealthSnapshot, compressionModel);
		}

		public uint Tick { get; set; }
	}

	public class TestCustomArchetypeSerializer : EntitySerializer<TestCustomArchetypeSerializer, TestCustomArchetypeSnapshot, TestCustomArchetypeSerializer.SharedData>
	{
		public struct SharedData
		{
			public ArchetypeChunkComponentType<Translation> TranslationArch;
			public ArchetypeChunkComponentType<Health>      HealthArch;

			public BufferFromEntity<TestCustomArchetypeSnapshot> SnapshotFromEntity;
		}

		public struct Exclude : IComponentData
		{
		}

		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(3, Allocator.Temp)
			{
				[0] = typeof(CustomArchetypeTag),
				[1] = typeof(Translation),
				[2] = typeof(Health)
			};

		[BurstCompile]
		public static void Serialize(uint systemId, ref SerializeClientData jobData, ref DataStreamWriter writer)
		{
			var sharedData = GetShared();
			var chunks     = GetSerializerChunkData().Array;
			for (var i = 0; i != chunks.Length; i++)
			{
				var chunk            = chunks[i];
				var ghostArray       = chunk.GetNativeArray(jobData.GhostType);
				var translationArray = chunk.GetNativeArray(sharedData.TranslationArch);
				var healthArray      = chunk.GetNativeArray(sharedData.HealthArch);

				for (var ent = 0; ent != chunk.Count; ent++)
				{
					jobData.TryGetSnapshot(ghostArray[ent].Value, out var snapshot);

					ref var baseline = ref snapshot.TryGetSystemData<TestCustomArchetypeSnapshot>(systemId, out var success);
					if (!success)
					{
						baseline = ref snapshot.AllocateSystemData<TestCustomArchetypeSnapshot>(systemId);
						baseline = default;
					}

					var newSnapshot = new TestCustomArchetypeSnapshot();
					newSnapshot.TranslationSnapshot.SynchronizeFrom(translationArray[ent]);
					newSnapshot.HealthSnapshot.SynchronizeFrom(healthArray[ent]);
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
			for (var ent = 0; ent < ghostArray.Length; ent++)
			{
				var snapshotArray = sharedData.SnapshotFromEntity[jobData.GhostToEntityMap[ghostArray[ent]]];

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TestCustomArchetypeSnapshot);
				newSnapshot.Tick = tick;
				newSnapshot.ReadFrom(ref ctx, reader, ref snapshotArray.GetLastBaseline(), jobData.NetworkCompressionModel);

				snapshotArray.Add(newSnapshot);
			}
		}

		public override ComponentType ExcludeComponent => ComponentType.ReadWrite<Exclude>();

		public override FunctionPointer<OnSerializeSnapshot>   SerializeDelegate   => m_SerializeDelegate.Get();
		public override FunctionPointer<OnDeserializeSnapshot> DeserializeDelegate => m_DeserializeDelegate.Get();

		private EntityQuery                          m_WithoutComponentQuery;
		private BurstDelegate<OnSerializeSnapshot>   m_SerializeDelegate;
		private BurstDelegate<OnDeserializeSnapshot> m_DeserializeDelegate;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_WithoutComponentQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(TestCustomArchetypeSnapshot)},
				None = new ComponentType[] {typeof(CustomArchetypeTag)}
			});

			m_SerializeDelegate   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			m_DeserializeDelegate = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			EntityManager.AddComponent(m_WithoutComponentQuery, typeof(Translation));
			EntityManager.AddComponent(m_WithoutComponentQuery, typeof(Health));
			EntityManager.AddComponent(m_WithoutComponentQuery, typeof(CustomArchetypeTag));

			return base.OnUpdate(inputDeps);
		}

		public override void OnBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.TranslationArch = GetArchetypeChunkComponentType<Translation>(true);
			sharedData.HealthArch      = GetArchetypeChunkComponentType<Health>(true);
		}

		public override void OnBeginDeserialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.SnapshotFromEntity = GetBufferFromEntity<TestCustomArchetypeSnapshot>();
		}
	}
}
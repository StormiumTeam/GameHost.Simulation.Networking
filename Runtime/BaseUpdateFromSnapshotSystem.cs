using System.Collections.Generic;
using Unity.NetCode;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace DefaultNamespace
{
	public interface IComponentFromSnapshot<TSnapshot> : IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
		void Set(TSnapshot snapshot, NativeHashMap<int, GhostEntity> ghostMap);
	}

	public interface ISnapshotFromComponent<TSnapshotData, in TComponent> : ISnapshotData<TSnapshotData>
		where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		where TComponent : struct, IComponentData
	{
		void Set(TComponent component);
	}

	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(GhostSpawnSystemGroup))]
	public abstract class BaseUpdateFromSnapshotSystem<TSnapshot, TComponent> : JobComponentSystem
		where TComponent : struct, IComponentFromSnapshot<TSnapshot>
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
		//[BurstCompile]
		struct UpdateJob : IJobForEachWithEntity<TComponent>
		{
			[ReadOnly] public BufferFromEntity<TSnapshot> SnapshotFromEntity;

			[NativeDisableContainerSafetyRestriction]
			public NativeHashMap<int, GhostEntity> GhostMap;

			public uint TargetTick;

			public void Execute(Entity entity, int index, ref TComponent component)
			{
				SnapshotFromEntity[entity].GetDataAtTick(TargetTick, out var snapshotData);

				component.Set(snapshotData, GhostMap);
			}
		}

		private EntityQuery m_Query;

		protected override void OnCreate()
		{
			base.OnCreate();
			m_Query = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new UpdateJob()
			{
				SnapshotFromEntity = GetBufferFromEntity<TSnapshot>(),
				GhostMap           = World.GetExistingSystem<GhostReceiveSystemGroup>().GhostEntityMap,
				TargetTick         = NetworkTimeSystem.predictTargetTick
			}.Schedule(m_Query, inputDeps);
		}
	}
}
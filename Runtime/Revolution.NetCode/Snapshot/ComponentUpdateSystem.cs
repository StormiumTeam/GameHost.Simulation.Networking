using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Revolution.NetCode
{
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystem<TComponent, TSnapshot> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>
	{
		private struct Job : IJobForEach_BC<TSnapshot, TComponent>
		{
			[ReadOnly]
			public DeserializeClientData JobData;

			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshot, ref TComponent component)
			{
				if (snapshot.Length > 0)
					snapshot[snapshot.Length - 1].SynchronizeTo(ref component, in JobData);
			}
		}

		private EntityQuery           m_UpdateQuery;
		private SnapshotReceiveSystem m_ReceiveSystem;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_UpdateQuery = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
			m_ReceiveSystem = World.GetOrCreateSystem<SnapshotReceiveSystem>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new Job
			{
				JobData = m_ReceiveSystem.JobData
			}.Schedule(m_UpdateQuery, inputDeps);
		}
	}
	
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystem<TComponent, TSnapshot, TSetup> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>
		where TSetup : struct, ISetup
	{
		private struct Job : IJobForEach_BC<TSnapshot, TComponent>
		{
			[ReadOnly]
			public DeserializeClientData JobData;

			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshot, ref TComponent component)
			{
				if (snapshot.Length > 0)
					snapshot[snapshot.Length - 1].SynchronizeTo(ref component, in JobData);
			}
		}

		private EntityQuery           m_UpdateQuery;
		private SnapshotReceiveSystem m_ReceiveSystem;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_UpdateQuery   = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
			m_ReceiveSystem = World.GetOrCreateSystem<SnapshotReceiveSystem>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new Job
			{
				JobData = m_ReceiveSystem.JobData
			}.Schedule(m_UpdateQuery, inputDeps);
		}
	}
}
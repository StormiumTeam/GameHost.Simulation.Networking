using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Revolution.NetCode
{
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystemDirect<TComponent, TSnapshot> : ComponentUpdateSystemDirect<TComponent, TSnapshot, DefaultSetup>
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>
	{
	}

	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystemDirect<TComponent, TSnapshot, TSetup> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>
		where TSetup : struct, ISetup
	{
		private struct JobDirect : IJobForEach_BC<TSnapshot, TComponent>
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
			return new JobDirect
			{
				JobData = m_ReceiveSystem.JobData
			}.Schedule(m_UpdateQuery, inputDeps);
		}
	}
	
	// -------------------------------------------------------------- //
	// INTERPOLATED
	// -------------------------------------------------------------- //
	
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystemInterpolated<TComponent, TSnapshot> : ComponentUpdateSystemInterpolated<TComponent, TSnapshot, DefaultSetup>
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>, IInterpolatable<TSnapshot>
	{
	}

	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateAfter(typeof(SnapshotReceiveSystem))]
	public class ComponentUpdateSystemInterpolated<TComponent, TSnapshot, TSetup> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IInterpolatable<TSnapshot>
		where TSetup : struct, ISetup
	{
		protected virtual bool IsPredicted => false;
		
		private struct JobInterpolated : IJobForEach_BC<TSnapshot, TComponent>
		{
			[ReadOnly]
			public DeserializeClientData JobData;

			public uint TargetTick;

			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshot, ref TComponent component)
			{
				if (!snapshot.GetDataAtTick(TargetTick, out var snapshotData))
					return;

				if (snapshot.Length > 0)
					snapshot[snapshot.Length - 1].SynchronizeTo(ref component, in JobData);
			}
		}
		
		private EntityQuery           m_UpdateQuery;
		private SnapshotReceiveSystem m_ReceiveSystem;
		private NetworkTimeSystem m_NetworkTimeSystem;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_UpdateQuery   = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
			m_ReceiveSystem = World.GetOrCreateSystem<SnapshotReceiveSystem>();
			m_NetworkTimeSystem = World.GetOrCreateSystem<NetworkTimeSystem>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new JobInterpolated
			{
				JobData    = m_ReceiveSystem.JobData,
				TargetTick = IsPredicted ? m_NetworkTimeSystem.predictTargetTick : m_NetworkTimeSystem.interpolateTargetTick
			}.Schedule(m_UpdateQuery, inputDeps);
		}
	}
}
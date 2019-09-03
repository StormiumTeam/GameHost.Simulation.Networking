using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Revolution
{
	public class ComponentUpdateSystem<TComponent, TSnapshot> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent>
	{
		private struct Job : IJobForEach_BC<TSnapshot, TComponent>
		{
			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshot, ref TComponent component)
			{
				if (snapshot.Length > 0)
					snapshot[snapshot.Length - 1].SynchronizeTo(ref component);
			}
		}

		private EntityQuery m_UpdateQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_UpdateQuery = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new Job
			{

			}.Schedule(m_UpdateQuery, inputDeps);
		}
	}
}
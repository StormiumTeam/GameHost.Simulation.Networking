using Unity.NetCode;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace DefaultNamespace
{
	public interface IComponentFromSnapshot<TSnapshot> : IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
		void Set(TSnapshot snapshot);
	}

	public interface ISnapshotFromComponent<TSnapshotData, in TComponent> : ISnapshotData<TSnapshotData>
		where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		where TComponent : struct, IComponentData
	{
		void Set(TComponent component);
	}

	[UpdateInGroup(typeof(GhostReceiveSystemGroup))]
	public abstract class BaseUpdateFromSnapshotSystem<TSnapshot, TComponent> : JobComponentSystem 
		where TComponent : struct, IComponentFromSnapshot<TSnapshot>
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
		[BurstCompile]
		struct UpdateJob : IJobForEachWithEntity<TComponent>
		{
			[ReadOnly] public BufferFromEntity<TSnapshot> SnapshotFromEntity;
			public uint TargetTick;
			
			public void Execute(Entity entity, int index, ref TComponent component)
			{
				SnapshotFromEntity[entity].GetDataAtTick(TargetTick, out var snapshotData);
				
				component.Set(snapshotData);
			}
		}
		
		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return new UpdateJob()
			{
				SnapshotFromEntity = GetBufferFromEntity<TSnapshot>(),
				TargetTick         = NetworkTimeSystem.predictTargetTick
			}.Schedule(this, inputDeps);
		}
	}
}
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace Revolution
{
	public interface IReadWriteSnapshot<TSnapshot> : IRwSnapshotComplement<TSnapshot>, ISnapshotData<TSnapshot>
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
	}

	public interface IRwSnapshotComplement<TSnapshot>
		where TSnapshot : struct, ISnapshotData<TSnapshot>
	{
		void WriteTo(DataStreamWriter              writer, ref TSnapshot    baseline, NetworkCompressionModel compressionModel);
		void ReadFrom(ref DataStreamReader.Context ctx,    DataStreamReader reader,   ref TSnapshot           baseline, NetworkCompressionModel compressionModel);
	}

	public interface ISynchronizeImpl<TComponent>
		where TComponent : struct, IComponentData
	{
		void SynchronizeFrom(in TComponent component);
		void SynchronizeTo(ref  TComponent component);
	}

	[UpdateInGroup(typeof(SnapshotWithDelegateSystemGroup))]
	public abstract class ComponentSnapshotSystemBase<TComponent, TSnapshot, TSharedData> :
		EntitySerializer<ComponentSnapshotSystemBase<TComponent, TSnapshot, TSharedData>,
			TSnapshot,
			TSharedData>

		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent>, IRwSnapshotComplement<TSnapshot>
		where TComponent : struct, IComponentData
		where TSharedData : struct
	{
		private EntityQuery        m_EntityWithoutComponentQuery;

		internal abstract void SystemBeginSerialize(Entity                         entity);
		internal abstract void SystemBeginDeserialize(Entity                       entity);

		protected override void OnCreate()
		{
			base.OnCreate();
			
			m_EntityWithoutComponentQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(TSnapshot)},
				None = new ComponentType[] {typeof(TComponent)}
			});
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			if (!m_EntityWithoutComponentQuery.IsEmptyIgnoreFilter)
			{
				EntityManager.AddComponent(m_EntityWithoutComponentQuery, typeof(TComponent));
			}

			return base.OnUpdate(inputDeps);
		}

		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory)
			{
				[0] = ComponentType.ReadWrite<TComponent>()
			};

		public sealed override void OnBeginSerialize(Entity entity)
		{
			SystemBeginSerialize(entity);
		}

		public sealed override void OnBeginDeserialize(Entity entity)
		{
			SystemBeginDeserialize(entity);
		}
	}
}
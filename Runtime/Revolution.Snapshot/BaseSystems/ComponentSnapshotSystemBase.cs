using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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

	public interface ISetup
	{
		void BeginSetup(JobComponentSystem system
#if ENABLE_UNITY_COLLECTIONS_CHECKS
		                , AtomicSafetyHandle safetyHandle
#endif
		);
	}

	public struct DefaultSetup : ISetup
	{
		public void BeginSetup(JobComponentSystem system
#if ENABLE_UNITY_COLLECTIONS_CHECKS
		                       , AtomicSafetyHandle safetyHandle
#endif
		)
		{
		}
	}

	public interface ISynchronizeImpl<TComponent, TSetup>
		where TComponent : struct, IComponentData
		where TSetup : ISetup
	{
		void SynchronizeFrom(in TComponent component, in TSetup                setup, in SerializeClientData serializeData);
		void SynchronizeTo(ref  TComponent component, in DeserializeClientData deserializeData);
	}

	public interface ISynchronizeImpl<TComponent> : ISynchronizeImpl<TComponent, DefaultSetup>
		where TComponent : struct, IComponentData
	{
	}

	public interface IPredictable<T> : IInterpolatable<T>
		where T : struct
	{
		void PredictDelta(uint tick, ref T baseline1, ref T baseline2);
	}

	public abstract class ComponentSnapshotSystemBase<TComponent, TSnapshot, TSetup, TSharedData> :
		EntitySerializer<ComponentSnapshotSystemBase<TComponent, TSnapshot, TSetup, TSharedData>,
			TSnapshot,
			TSharedData>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IRwSnapshotComplement<TSnapshot>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISetup
		where TSharedData : struct
	{
		private EntityQuery m_EntityWithoutComponentQuery;

		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory)
			{
				[0] = ComponentType.ReadWrite<TComponent>()
			};

		internal abstract void SystemBeginSerialize(Entity   entity);
		internal abstract void SystemBeginDeserialize(Entity entity);

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
			if (!m_EntityWithoutComponentQuery.IsEmptyIgnoreFilter) EntityManager.AddComponent(m_EntityWithoutComponentQuery, typeof(TComponent));

			return base.OnUpdate(inputDeps);
		}

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
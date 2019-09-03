using System;
using Revolution;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

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
		private BurstDelegate<OnSerializeSnapshot>   m_SerializeDelegate;
		private BurstDelegate<OnDeserializeSnapshot> m_DeserializeDelegate;

		private EntityQuery        m_EntityWithoutComponentQuery;
		private AtomicSafetyHandle m_BufferSafetyHandle;

		internal abstract void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize);
		internal abstract void SystemBeginSerialize(Entity                         entity);
		internal abstract void SystemBeginDeserialize(Entity                       entity);

		protected override void OnCreate()
		{
			base.OnCreate();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			m_BufferSafetyHandle = AtomicSafetyHandle.Create();
#endif

			GetDelegates(out m_SerializeDelegate, out m_DeserializeDelegate);
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

		public override FunctionPointer<OnSerializeSnapshot>   SerializeDelegate   => m_SerializeDelegate.Get();
		public override FunctionPointer<OnDeserializeSnapshot> DeserializeDelegate => m_DeserializeDelegate.Get();

		public sealed override void OnBeginSerialize(Entity entity)
		{
			SystemBeginSerialize(entity);
		}

		public sealed override void OnBeginDeserialize(Entity entity)
		{
			SystemBeginDeserialize(entity);
		}

		public unsafe void SetEmptySafetyHandle(ref BufferFromEntity<TSnapshot> bfe)
		{
			// remove safety... (the array goes only writeonly for some weird reasons)
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref bfe),
				UnsafeUtility.AddressOf(ref m_BufferSafetyHandle),
				sizeof(AtomicSafetyHandle));

			UnsafeUtility.MemCpy((byte*) UnsafeUtility.AddressOf(ref bfe) + sizeof(AtomicSafetyHandle),
				UnsafeUtility.AddressOf(ref m_BufferSafetyHandle),
				sizeof(AtomicSafetyHandle));
#endif
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.Release(m_BufferSafetyHandle);
#endif
		}
	}
}
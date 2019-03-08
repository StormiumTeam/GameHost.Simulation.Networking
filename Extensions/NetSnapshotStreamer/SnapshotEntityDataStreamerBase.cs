using System.Runtime.CompilerServices;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public abstract class SnapshotEntityDataStreamerBase<TState> : SnapshotDataStreamerBase
        where TState : struct, IComponentData
    {
        private int m_EntityVersion;

        public ComponentType StateType;
        public ComponentType ChangedType;

        protected ComponentDataFromEntity<TState>              States;
        protected ComponentDataFromEntity<DataChanged<TState>> Changed;

        private ComponentDataFromEntityBurstExtensions.CallExistsAsBurst m_StateExistsBurst;
        private ComponentDataFromEntityBurstExtensions.CallExistsAsBurst m_ChangedStateExistsBurst;

        private ComponentGroup m_EntitiesWithoutDataChanged;

        static DataBufferMarker WriteDataSafe(ref DataBufferWriter writer, int val)
        {
            return default;
        }

        protected override void OnCreateManager()
        {
            base.OnCreateManager();

            StateType   = ComponentType.Create<TState>();
            ChangedType = ComponentType.Create<DataChanged<TState>>();

            m_StateExistsBurst        = GetExistsCall<TState>();
            m_ChangedStateExistsBurst = GetExistsCall<DataChanged<TState>>();

            World.GetOrCreateManager<DataChangedSystem<TState>>();

            m_EntityVersion = -1;

            UpdateComponentDataFromEntity();

            m_EntitiesWithoutDataChanged = GetComponentGroup(ComponentType.Create<TState>(), ComponentType.Exclude<DataChanged<TState>>());
        }

        protected override JobHandle OnUpdate(JobHandle job)
        {
            base.OnUpdate(job);

            using (var entityArray = m_EntitiesWithoutDataChanged.ToEntityArray(Allocator.TempJob))
            {
                for (var i = 0; i != entityArray.Length; i++)
                    EntityManager.AddComponentData(entityArray[i], new DataChanged<TState> {IsDirty = 1});
            }

            return job;
        }

        protected ComponentDataFromEntityBurstExtensions.CallExistsAsBurst GetExistsCall<T>()
            where T : struct, IComponentData
        {
            return ComponentDataFromEntityBurstExtensions.CreateCall<T>.Exists();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool StateExists(Entity entity)
        {
            return States.CallExists(m_StateExistsBurst, entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected bool ChangedStateExists(Entity entity)
        {
            return Changed.CallExists(m_ChangedStateExistsBurst, entity);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void UpdateComponentDataFromEntity()
        {
            m_EntityVersion = EntityManager.Version;

            Profiler.BeginSample("Update GetComponentDataFromEntity");
            States  = GetComponentDataFromEntity<TState>();
            Changed = GetComponentDataFromEntity<DataChanged<TState>>();
            Profiler.EndSample();
        }
    }

    public abstract class SnapshotEntityDataStreamerBufferBase<TState> : SnapshotDataStreamerBase
        where TState : struct, IBufferElementData
    {
        private int m_EntityVersion;

        public ComponentType StateType;

        protected BufferFromEntity<TState> States;

        protected override void OnCreateManager()
        {
            base.OnCreateManager();

            StateType = ComponentType.Create<TState>();
        }
    }
}
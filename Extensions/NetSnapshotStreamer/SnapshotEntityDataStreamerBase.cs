using System.Runtime.CompilerServices;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Entities;
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

        static DataBufferMarker WriteDataSafe(ref DataBufferWriter writer, int val)
        {
            return default;
        }

        protected override unsafe void OnCreateManager()
        {
            base.OnCreateManager();

            StateType   = ComponentType.Create<TState>();
            ChangedType = ComponentType.Create<DataChanged<TState>>();

            m_StateExistsBurst        = GetExistsCall<TState>();
            m_ChangedStateExistsBurst = GetExistsCall<DataChanged<TState>>();

            m_EntityVersion = -1;

            UpdateComponentDataFromEntity();
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
}
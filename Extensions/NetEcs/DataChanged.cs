using System;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public struct DataChanged<T> : IComponentData
        where T : struct, IComponentData
    {
        public T    Previous;
        public bool IsDirty;

        public unsafe bool Update(ref T next)
        {
            IsDirty  = UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref Previous), UnsafeUtility.AddressOf(ref next), UnsafeUtility.SizeOf<T>()) != 0;
            Previous = next;

            return IsDirty;
        }
    }

    public abstract class DataChangedSystemBase : ComponentSystem
    {
        public virtual Type Type { get; }
        public abstract string Name { get; }

        protected override void OnUpdate()
        {
        }

        internal abstract void Update(ref JobHandle jobHandle);
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public class DataChangedSystemGroup : ComponentSystemGroup
    {
        private int m_LastTypeCount;
        
        protected override void OnUpdate()
        {
            ScanForDataChangedComponents();
            
            JobHandle jobHandle = default;
            // ReSharper disable PossibleInvalidCastExceptionInForeachLoop
            foreach (DataChangedSystemBase sys in m_systemsToUpdate)
            {
                Profiler.BeginSample(sys.Name);
                sys.Update(ref jobHandle);
                Profiler.EndSample();
                if (World.QuitUpdate)
                    break;
            }

            jobHandle.Complete();
            // ReSharper restore PossibleInvalidCastExceptionInForeachLoop
        }

        private void ScanForDataChangedComponents()
        {
            if (TypeManager.GetTypeCount() == m_LastTypeCount)
                return;

            m_LastTypeCount = TypeManager.GetTypeCount();

            foreach (var type in TypeManager.AllTypes)
            {
                var managed = type.Type;
                if (managed == null)
                    continue;
                
                if (managed.IsGenericType && managed.GetGenericTypeDefinition() == typeof(DataChanged<>))
                {
                    var genericArg = managed.GenericTypeArguments[0];
                    var systemType = typeof(DataChangedSystem<>);
                    var genericSystemType = systemType.MakeGenericType(genericArg);

                    Debug.Log($"{genericArg} {genericSystemType}");
                    
                    var instance = World.GetOrCreateSystem(genericSystemType);
                    if (!m_systemsToUpdate.Contains((ComponentSystemBase) instance))
                    {
                        AddSystemToUpdateList((ComponentSystemBase) instance);
                    }
                }
            }
            
            this.SortSystemUpdateList();
        }
    }

    [UpdateInGroup(typeof(DataChangedSystemGroup))]
    [UsedImplicitly]
    public unsafe class DataChangedSystem<T> : DataChangedSystemBase
        where T : struct, IComponentData
    {
        private string m_Name = $"DataChangedSystem<{typeof(T).Name}>";
        
        public override Type Type => typeof(T);
        public override string Name => m_Name;

        [BurstCompile]
        private struct UpdateData : IJobChunk
        {
            public            uint                                        LastSystemVersion;
            [ReadOnly] public ArchetypeChunkComponentType<T>              DataType;
            public            ArchetypeChunkComponentType<DataChanged<T>> ChangedType;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                if (!chunk.DidChange(DataType, LastSystemVersion))
                    return;

                var chunkData     = chunk.GetNativeArray(DataType);
                var chunkChanged  = chunk.GetNativeArray(ChangedType);
                var instanceCount = chunk.Count;

                for (int i = 0; i < instanceCount; i++)
                {
                    var     data    = chunkData[i];
                    ref var changed = ref UnsafeUtilityEx.ArrayElementAsRef<DataChanged<T>>(chunkChanged.GetUnsafePtr(), i);

                    changed.Update(ref data);
                }
            }
        }

        private EntityQuery m_Group;

        protected override void OnCreate()
        {
            m_Group = GetEntityQuery(typeof(T), typeof(DataChanged<T>));
        }

        internal override void Update(ref JobHandle jobHandle)
        {
            var dataType    = GetArchetypeChunkComponentType<T>(true);
            var changedType = GetArchetypeChunkComponentType<DataChanged<T>>(false);

            var rotationsSpeedRotationJob = new UpdateData
            {
                LastSystemVersion = LastSystemVersion,
                DataType          = dataType,
                ChangedType       = changedType
            };

            jobHandle = rotationsSpeedRotationJob.Schedule(m_Group, jobHandle);
        }
    }
}
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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
        protected override void OnUpdate()
        {
        }

        internal abstract void Update(ref JobHandle jobHandle);
    }

    public class DataChangedSystemGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
            JobHandle jobHandle = default;
            // ReSharper disable PossibleInvalidCastExceptionInForeachLoop
            foreach (DataChangedSystemBase sys in m_systemsToUpdate)
            {
                sys.Update(ref jobHandle);
                if (World.QuitUpdate)
                    break;
            }

            jobHandle.Complete();
            // ReSharper restore PossibleInvalidCastExceptionInForeachLoop
        }
    }

    [UpdateInGroup(typeof(DataChangedSystemGroup))]
    [UsedImplicitly]
    public unsafe class DataChangedSystem<T> : DataChangedSystemBase
        where T : struct, IComponentData
    {
        [BurstCompile]
        private struct UpdateData : IJobChunk
        {
            public uint                                        LastSystemVersion;
            public ArchetypeChunkComponentType<T>              DataType;
            public ArchetypeChunkComponentType<DataChanged<T>> ChangedType;

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

        private ComponentGroup m_Group;

        protected override void OnCreateManager()
        {
            m_Group = GetComponentGroup(typeof(T), typeof(DataChanged<T>));
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
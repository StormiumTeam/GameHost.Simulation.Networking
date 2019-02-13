using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine.Experimental.PlayerLoop;

namespace StormiumShared.Core.Networking
{
    public struct DataChanged<T> : IComponentData
        where T : struct, IComponentData
    {
        public T Previous;
        public byte IsDirty;
        
        public unsafe int Update(ref T next)
        {   
            IsDirty = (byte)(UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref Previous), UnsafeUtility.AddressOf(ref next), UnsafeUtility.SizeOf<T>()) == 0 ? 0 : 1);
            Previous = next;
            
            return (int) IsDirty;
        }
    }

    [UpdateInGroup(typeof(UpdateLoop.UpdateDataChangeComponents))]
    [DisableAutoCreation]
    [UsedImplicitly]
    public class DataChangedSystem<T> : ComponentSystem
        where T : struct, IComponentData
    {
        private ComponentGroup m_Group;

        protected override void OnCreateManager()
        {
            m_Group = GetComponentGroup(typeof(T), typeof(DataChanged<T>));
        }

        protected override void OnUpdate()
        {
            var length = m_Group.CalculateLength();
            var dataArray = m_Group.GetComponentDataArray<T>();
            var changedArray = m_Group.GetComponentDataArray<DataChanged<T>>();
            for (var i = 0; i != length; i++)
            {
                var data = dataArray[i];
                var changed = changedArray[i];

                changed.Update(ref data);

                changedArray[i] = changed;
            }
        }
    }
}
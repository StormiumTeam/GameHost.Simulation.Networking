using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    public static unsafe class ComponentDataFromEntityBurstExtensions
    {
        public static class CreateCall<T> where T : struct, IComponentData
        {
            public static CallExistsAsBurst Exists()
            {
                return BurstCompiler.CompileDelegate<CallExistsAsBurst>(InternalExists);
            }

            private static bool InternalExists(void* data, Entity entity)
            {
                UnsafeUtility.CopyPtrToStructure(data, out ComponentDataFromEntity<T> dataFromEntity);

                return dataFromEntity.Exists(entity);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CallExists<T>(this ref ComponentDataFromEntity<T> dataFromEntity, CallExistsAsBurst call, Entity entity)
            where T : struct, IComponentData
        {
            return call(Unsafe.AsPointer(ref dataFromEntity), entity);
        }

        public delegate bool CallExistsAsBurst(void* data, Entity entity);
    }
}
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace package.stormiumteam.networking.runtime
{
    public unsafe struct TotalUnmanagedData<T> : IDisposable where T : struct
    {
        public Allocator Allocator;
        
        [NativeDisableUnsafePtrRestriction]
        public void*     Value;

        public TotalUnmanagedData(Allocator allocator)
        {
            Allocator = Allocator.None;
            Value = default(void*);
            
            InternalAllocate(allocator);
        }

        [BurstDiscard]
        private void InternalAllocate(Allocator allocator)
        {
            Allocator = allocator;
            Value     = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<T>(), Allocator);
        }

        public void Set(T value)
        {
            UnsafeUtility.MemCpy(Value, UnsafeUtility.AddressOf(ref value), UnsafeUtility.SizeOf<T>());
        }

        public void Dispose()
        {
            UnsafeUtility.Free(Value, Allocator);
        }
        
        public static explicit operator T(TotalUnmanagedData<T> data)
        {
            T value;
            UnsafeUtility.CopyPtrToStructure(data.Value, out value);
            return value;
        }
    }
}
using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.NetCode
{
    internal unsafe struct ResizableListData
    {
        [NativeDisableUnsafePtrRestriction] public void* Buffer;
        public int   Length;
        public int   Capacity;
    }

    public unsafe struct ResizableList<T> : IDisposable
        where T : struct
    {
        public int Length
        {
            get => m_Data->Length;
            set => m_Data->Length = value;
        }

        public int Capacity
        {
            get => m_Data->Capacity;
            set
            {
                var capacity  = math.max(value, 8);
                var newBuffer = (byte*) UnsafeUtility.Malloc(capacity * UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<byte>(), m_Allocator);

                UnsafeUtility.MemCpy(newBuffer, m_Data->Buffer, m_Data->Length * UnsafeUtility.SizeOf<T>());
                UnsafeUtility.Free(m_Data->Buffer, m_Allocator);

                m_Data->Capacity = capacity;
                m_Data->Buffer   = newBuffer;
            }
        }

        public T this[int index]
        {
            get => UnsafeUtility.ReadArrayElement<T>(m_Data->Buffer, index);
            set => UnsafeUtility.WriteArrayElement<T>(m_Data->Buffer, index, value);
        }

        [NativeDisableUnsafePtrRestriction] private ResizableListData* m_Data;
        private Allocator          m_Allocator;

        public ResizableList(Allocator allocator, int size)
        {
            m_Allocator = allocator;
            m_Data      = (ResizableListData*) UnsafeUtility.Malloc(UnsafeUtility.SizeOf<ResizableListData>(), UnsafeUtility.AlignOf<ResizableListData>(), m_Allocator);

            var capacity  = math.max(size, 8);
            var newBuffer = (byte*) UnsafeUtility.Malloc(capacity * UnsafeUtility.SizeOf<T>(), UnsafeUtility.AlignOf<byte>(), m_Allocator);

            m_Data->Capacity = capacity;
            m_Data->Buffer   = newBuffer;
            m_Data->Length   = 0;
        }

        public void Add(T v)
        {
            if (m_Data->Length >= m_Data->Capacity)
                Capacity = m_Data->Length + m_Data->Capacity * 2;

            this[m_Data->Length++] = v;
        }

        public void Clear()
        {
            Length = 0;
        }

        public void RemoveAtSwapBack(int index)
        {
            var newLength = m_Data->Length - 1;
            this[index]    = this[newLength];
            m_Data->Length = newLength;
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Data->Buffer, m_Allocator);
            UnsafeUtility.Free(m_Data, m_Allocator);
        }
    }
}
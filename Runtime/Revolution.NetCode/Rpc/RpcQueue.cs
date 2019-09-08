using System;
using Revolution.NetCode;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Revolution.NetCode
{
    public struct RpcQueue<T> where T : struct, IRpcCommand
    {
        public RpcQueue<T> Null => default(RpcQueue<T>);

        public int RpcType { get; internal set; }

        public unsafe void Schedule(DynamicBuffer<OutgoingRpcDataStreamBufferComponent> buffer, T data)
        {
            if (RpcType == 0)
                throw new InvalidOperationException("A RpcQueue wasn't initialized!");

            var writer = new DataStreamWriter(256, Allocator.Temp);
            writer.Write(RpcType);
            data.WriteTo(writer);
            var prevLen = buffer.Length;
            buffer.ResizeUninitialized(buffer.Length + writer.Length);
            var ptr = (byte*) buffer.GetUnsafePtr();
            ptr += prevLen;
            UnsafeUtility.MemCpy(ptr, writer.GetUnsafeReadOnlyPtr(), writer.Length);
        }
    }
}
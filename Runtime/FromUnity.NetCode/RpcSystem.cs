using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using package.stormiumteam.networking.runtime.Rpc;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Unity.NetCode
{
    public interface RpcCommand
    {
        void Execute(Entity               connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex);
        void Serialize(DataStreamWriter   writer);
        void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx);
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    public abstract class RegisterRpc<TRpc> : ComponentSystem
        where TRpc : struct, IRpcBase<TRpc>
    {
        public RpcBase.Header Header { get; private set; }

        protected override void OnCreate()
        {
            base.OnCreate();

            Header = World.GetOrCreateSystem<RpcSystem>().GetHeader<TRpc>();
        }

        public unsafe TRpc New()
        {
            var header = Header;
            var rpc    = default(TRpc);

            UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref rpc), UnsafeUtility.AddressOf(ref header), UnsafeUtility.SizeOf<RpcBase.Header>());

            return rpc;
        }

        protected override void OnUpdate()
        {
        }
    }

    public struct RpcQueue<T> where T : struct, RpcCommand
    {
        internal int rpcType;

        public unsafe void Schedule(DynamicBuffer<OutgoingRpcDataStreamBufferComponent> buffer, T data)
        {
            DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
            if (buffer.Length == 0)
                writer.Write((byte) NetworkStreamProtocol.Rpc);
            writer.Write(rpcType);
            data.Serialize(writer);
            var prevLen = buffer.Length;
            buffer.ResizeUninitialized(buffer.Length + writer.Length);
            byte* ptr = (byte*) buffer.GetUnsafePtr();
            ptr += prevLen;
            UnsafeUtility.MemCpy(ptr, writer.GetUnsafeReadOnlyPtr(), writer.Length);
        }
    }

    public struct RpcBaseQueue<T> where T : struct, IRpcBase<T>
    {
        public const int rpcType = 1;

        internal RpcBase.Header Header;

        public unsafe void Schedule(DynamicBuffer<OutgoingRpcDataStreamBufferComponent> buffer, T data)
        {
            DataStreamWriter writer = new DataStreamWriter(128, Allocator.Temp);
            if (buffer.Length == 0)
                writer.Write((byte) NetworkStreamProtocol.Rpc);
            writer.Write(rpcType);
            writer.Write(Header.Id);
            Header.Serialize(ref data, writer);
            var prevLen = buffer.Length;
            buffer.ResizeUninitialized(buffer.Length + writer.Length);
            byte* ptr = (byte*) buffer.GetUnsafePtr();
            ptr += prevLen;
            UnsafeUtility.MemCpy(ptr, writer.GetUnsafeReadOnlyPtr(), writer.Length);
        }
    }

    public struct OutgoingRpcDataStreamBufferComponent : IBufferElementData
    {
        public byte Value;
    }

    public struct IncomingRpcDataStreamBufferComponent : IBufferElementData
    {
        public byte Value;
    }

    [UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    public class RpcSystem : JobComponentSystem
    {
        private Type[]                                   m_RpcTypes;
        private EntityQuery                              m_RpcBufferGroup;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        private Dictionary<Type, int>           m_ManagedToUnmanaged = new Dictionary<Type, int>();
        private Dictionary<int, RpcBase.Header> m_RpcHeaders         = new Dictionary<int, RpcBase.Header>();

        /// <summary>
        /// Can we register any RPC systems?
        /// </summary>
        public bool CanRegister { get; set; }

        protected override void OnCreate()
        {
            m_RpcTypes = new Type[] {typeof(RpcSetNetworkId), typeof(RpcBaseType)};
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            Debug.Assert(UnsafeUtility.SizeOf<OutgoingRpcDataStreamBufferComponent>() == 1);
            Debug.Assert(UnsafeUtility.SizeOf<IncomingRpcDataStreamBufferComponent>() == 1);
#endif
            m_Barrier        = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_RpcBufferGroup = GetEntityQuery(ComponentType.ReadWrite<IncomingRpcDataStreamBufferComponent>());

            CanRegister = true;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            CanRegister = false;

            m_ManagedToUnmanaged.Clear();
            m_RpcHeaders.Clear();
        }

        struct RpcExecJob : IJobChunk
        {
            public            EntityCommandBuffer.Concurrent                                 commandBuffer;
            [ReadOnly] public ArchetypeChunkEntityType                                       entityType;
            public            ArchetypeChunkBufferType<IncomingRpcDataStreamBufferComponent> bufferType;

            [ReadOnly] public NativeList<RpcBase.Header> headers;

            public unsafe void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var entities     = chunk.GetNativeArray(entityType);
                var bufferAccess = chunk.GetBufferAccessor(bufferType);
                for (int i = 0; i < bufferAccess.Length; ++i)
                {
                    var              dynArray = bufferAccess[i];
                    DataStreamReader reader   = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) dynArray.GetUnsafePtr(), dynArray.Length);
                    var              ctx      = default(DataStreamReader.Context);
                    while (reader.GetBytesRead(ref ctx) < reader.Length)
                    {
                        int type = reader.ReadInt(ref ctx);
                        switch (type)
                        {
                            case 0:
                            {
                                var tmp = new RpcSetNetworkId();
                                tmp.Deserialize(reader, ref ctx);
                                tmp.Execute(entities[i], commandBuffer, chunkIndex);
                                break;
                            }
                            case 1: // custom
                            {
                                var customType = reader.ReadInt(ref ctx);
                                var header     = headers[customType];
                                var ptr        = UnsafeUtility.Malloc(header.Size, header.Align, Allocator.TempJob);
                                UnsafeUtility.MemCpy(ptr, UnsafeUtility.AddressOf(ref header), UnsafeUtility.SizeOf<RpcBase.Header>());
                                Debug.Log("copied");
                                
                                header.DeserializeFunction.Invoke(ptr, header.Size, (void*) &reader, ref ctx);
                                header.ExecuteFunction.Invoke(ptr, header.Size, entities[i], commandBuffer, chunkIndex);

                                UnsafeUtility.Free(ptr, Allocator.TempJob);
                                break;
                            }
                        }
                    }

                    dynArray.Clear();
                }
            }
        }

        protected override void OnStartRunning()
        {
            base.OnStartRunning();

            CanRegister = false;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            // Deserialize the command type from the reader stream
            // Execute the RPC
            var execJob = new RpcExecJob();
            execJob.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            execJob.entityType    = GetArchetypeChunkEntityType();
            execJob.bufferType    = GetArchetypeChunkBufferType<IncomingRpcDataStreamBufferComponent>();
            execJob.headers       = GetAllHeaders(Allocator.TempJob);
            var handle = execJob.Schedule(m_RpcBufferGroup, inputDeps);
            handle = execJob.headers.Dispose(handle);

            m_Barrier.AddJobHandleForProducer(handle);
            return handle;
        }

        public RpcQueue<T> GetRpcQueue<T>() where T : struct, RpcCommand
        {
            int t = 0;
            while (t < m_RpcTypes.Length && m_RpcTypes[t] != typeof(T))
                ++t;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (t >= m_RpcTypes.Length)
                throw new InvalidOperationException("Trying to get a rpc type which is not registered");
#endif
            return new RpcQueue<T> {rpcType = t};
        }

        public NativeList<RpcBase.Header> GetAllHeaders(Allocator tempJob)
        {
            var list = new NativeList<RpcBase.Header>(tempJob);
            foreach (var header in m_RpcHeaders.Values)
            {
                list.Add(header);
            }

            return list;
        }

        public unsafe RpcBase.Header GetHeader<T>() where T : struct, IRpcBase<T>
        {
            var type = typeof(T);
            if (m_ManagedToUnmanaged.ContainsKey(type))
                return m_RpcHeaders[m_ManagedToUnmanaged[type]];

            if (!CanRegister)
            {
                throw new Exception("Can not register RPC anymore!");
            }

            var i = m_RpcHeaders.Count;

            m_ManagedToUnmanaged[type] = i;
            m_RpcHeaders[i] = new RpcBase.Header
            {
                Id    = i,
                Size  = UnsafeUtility.SizeOf<T>(),
                Align = UnsafeUtility.AlignOf<T>(),
                SerializeFunction = new FunctionPointer<RpcBase.u_Serialize>(Marshal.GetFunctionPointerForDelegate(new RpcBase.u_Serialize((s, size, data) =>
                {
                    ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
                    output.Serialize(data);
                }))),
                DeserializeFunction = new FunctionPointer<RpcBase.u_Deserialize>(Marshal.GetFunctionPointerForDelegate(new RpcBase.u_Deserialize((void* s, int size, void* data, ref DataStreamReader.Context ctx) =>
                {
                    ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
                    UnsafeUtility.CopyPtrToStructure(data, out DataStreamReader r);

                    output.Deserialize(r, ref ctx);
                }))),
                ExecuteFunction = new FunctionPointer<RpcBase.u_Execute>(Marshal.GetFunctionPointerForDelegate(new RpcBase.u_Execute((s, size, connection, commandBuffer, jobIndex) =>
                {
                    ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
                    output.Execute(connection, commandBuffer, jobIndex);
                })))
            };

            return m_RpcHeaders[m_ManagedToUnmanaged[type]];
        }

        public RpcBaseQueue<T> GetRpcBaseQueue<T>() where T : struct, IRpcBase<T>
        {
            return new RpcBaseQueue<T> {Header = GetHeader<T>()};
        }
    }
}
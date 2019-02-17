using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public abstract unsafe class SnapshotEntityDataManualStreamer<TState, TWriteEntityPayload, TReadEntityPayload> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData
        where TWriteEntityPayload : struct, IWriteEntityDataPayload<TState>
        where TReadEntityPayload : struct, IReadEntityDataPayload<TState>
    {
        internal static SnapshotEntityDataManualStreamer<TState, TWriteEntityPayload, TReadEntityPayload> m_CurrentStreamer;
        internal static IntPtr                                                                            m_WriteDataForEntityOptimizedPtr;

        internal TWriteEntityPayload m_CurrentWritePayload;
        internal TReadEntityPayload  m_CurrentReadPayload;

        public virtual bool CanBurstWriteJob => true;
        public virtual bool CanBurstReadJob => true;

        protected abstract void UpdatePayloadW(ref TWriteEntityPayload current);
        protected abstract void UpdatePayloadR(ref TReadEntityPayload  current);
        
        [BurstCompile]
        struct WriteJob : IJob
        {
            public DataBufferWriter                           Buffer;
            public SnapshotReceiver                           Receiver;
            public StSnapshotRuntime                          Runtime;
            public int                                        EntityLength;

            public byte IsSameWriteType;
            
            [ReadOnly]
            public ComponentDataFromEntity<TState>                 States;
            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>>    Changes;
            
            [NativeDisableUnsafePtrRestriction]
            public TWriteEntityPayload cp;
            
            public void Execute()
            {
                const byte ReasonNoComponent = (byte) StreamerSkipReason.NoComponent;
                const byte ReasonDelta       = (byte) StreamerSkipReason.Delta;
                const byte ReasonNoSkip      = (byte) StreamerSkipReason.NoSkip;
                
                for (var i = 0; i != EntityLength; i++)
                {
                    var entity = Runtime.Entities[i].Source;
                    if (!States.Exists(entity))
                    {
                        Buffer.WriteByte(ReasonNoComponent);
                        continue;
                    }

                    var change = default(DataChanged<TState>);
                    change.IsDirty = 1;
                    
                    if (Changes.Exists(entity))
                        change = Changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(Receiver, change))
                    {
                        Buffer.WriteByte(ReasonDelta);
                        continue;
                    }

                    if (IsSameWriteType == 1)
                    {
                        var state = States[entity];
                        cp = Unsafe.As<TState, TWriteEntityPayload>(ref state);
                    }

                    Buffer.WriteByte(ReasonNoSkip);
                    cp.Write(i, entity, States, Changes, Buffer, Receiver, Runtime);
                }
            }
        }

        /*struct AddRequest
        {
            public Entity entity;
            public TState data;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType StateType;
            public ComponentType ChangedType;

            public DataBufferReader  Buffer;
            public SnapshotSender    Sender;
            public StSnapshotRuntime Runtime;
            public int               EntityLength;

            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            [WriteOnly]
            public NativeArray<int> CurrReadDataCursor;

            [WriteOnly]
            public NativeList<AddRequest> AddComponentRequests;
            
            [NativeDisableUnsafePtrRestriction]
            public TReadEntityPayload cp;

            public EntityCommandBuffer Ecb;

            public void Execute()
            {
                for (var index = 0; index != EntityLength; index++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    var skip        = Buffer.ReadValue<StreamerSkipReason>();

                    if (skip != StreamerSkipReason.NoSkip)
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        if (skip == StreamerSkipReason.NoComponent
                            && States.Exists(worldEntity))
                        {
                            Ecb.RemoveComponent(worldEntity, StateType);
                            // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                            if (Changes.Exists(worldEntity))
                            {
                                Ecb.RemoveComponent(worldEntity, ChangedType);
                            }
                        }

                        continue; // skip
                    }

                    if (!States.Exists(worldEntity))
                    {
                        AddComponentRequests.Add(new AddRequest {entity = worldEntity, data = newData});
                        continue;
                    }

                    cp.Read(index, worldEntity, States, ref Buffer, Sender, Runtime);
                }

                CurrReadDataCursor[0] = Buffer.CurrReadIndex;
            }
        }*/

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);
            UpdateComponentDataFromEntity();

            m_CurrentStreamer = this;
            UpdatePayloadW(ref m_CurrentWritePayload);

            Profiler.BeginSample("CallWriteData (Bursted)");
            //ManualStreamerBurst.CallWriteData(m_WriteDataBurst, buffer, receiver, runtime, entityLength, States, Changed, writeFunction, m_CurrentWritePayload);
            new WriteJob
            {
                Buffer       = buffer,
                Changes      = Changed,
                States       = States,
                EntityLength = entityLength,
                Receiver     = receiver,
                Runtime      = runtime,
                cp           = m_CurrentWritePayload,
                
                IsSameWriteType = Convert.ToByte(typeof(TState) == typeof(TWriteEntityPayload))
            }.Run();
            Profiler.EndSample();
            
            return buffer;
        }
 
        public override void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();
            UpdatePayloadR(ref m_CurrentReadPayload);

            for (var index = 0; index != length; index++)
            {
                var worldEntity = runtime.GetWorldEntityFromGlobal(index);
                var skip        = sysData.ReadValue<StreamerSkipReason>();

                if (skip != StreamerSkipReason.NoSkip)
                {
                    // If the component don't exist in the snapshot, also remove it from our world.
                    if (skip == StreamerSkipReason.NoComponent
                        && StateExists(worldEntity))
                    {
                        EntityManager.RemoveComponent<TState>(worldEntity);
                        // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                        if (ChangedStateExists(worldEntity))
                        {
                            EntityManager.RemoveComponent<DataChanged<TState>>(worldEntity);
                        }

                        UpdateComponentDataFromEntity();
                    }

                    continue; // skip
                }

                if (!StateExists(worldEntity))
                {
                    EntityManager.AddComponent(worldEntity, typeof(TState));
                    UpdateComponentDataFromEntity();
                }

                m_CurrentReadPayload.Read(index, worldEntity, States, ref sysData, sender, runtime);
            }
        }
    }

    public abstract class SnapshotEntityDataManualStreamer<TState, TMultiEntityPayload> : SnapshotEntityDataManualStreamer<TState, TMultiEntityPayload, TMultiEntityPayload>
        where TState : struct, IComponentData
        where TMultiEntityPayload : struct, IMultiEntityDataPayload<TState>
    {
        protected abstract void UpdatePayload(ref TMultiEntityPayload current);

        protected override void UpdatePayloadR(ref TMultiEntityPayload current)
        {
            UpdatePayload(ref current);
        }

        protected override void UpdatePayloadW(ref TMultiEntityPayload current)
        {
            UpdatePayload(ref current);
        }
    }

    public abstract class SnapshotEntityDataManualStreamer<TState> : SnapshotEntityDataManualStreamer<TState, TState, TState>
        where TState : struct, IComponentData, IWriteEntityDataPayload<TState>, IReadEntityDataPayload<TState>
    {
        protected override void UpdatePayloadW(ref TState current)
        {
        }

        protected override void UpdatePayloadR(ref TState current)
        {   
        }
    }

    public abstract class SnapshotEntityDataManualValueTypeStreamer<TState> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData, ISerializableAsPayload
    {
        [BurstCompile]
        private struct WriteJob : IJob
        {
            public DataBufferWriter  Buffer;
            public SnapshotReceiver  Receiver;
            public StSnapshotRuntime Runtime;
            public int               EntityLength;

            [ReadOnly]
            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            public void Execute()
            {
                for (var i = 0; i != EntityLength; i++)
                {
                    var entity = Runtime.Entities[i].Source;
                    if (!States.Exists(entity))
                    {
                        Buffer.WriteUnmanaged(StreamerSkipReason.NoComponent);
                        continue;
                    }

                    var change = default(DataChanged<TState>);
                    change.IsDirty = 1;

                    if (Changes.Exists(entity))
                        change = Changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(Receiver, change))
                    {
                        Buffer.WriteUnmanaged(StreamerSkipReason.Delta);
                        continue;
                    }

                    Buffer.WriteUnmanaged(StreamerSkipReason.NoSkip);
                    States[entity].Write(ref Buffer, Receiver, Runtime);
                }
            }
        }

        private struct AddRequest
        {
            public Entity entity;
            public TState data;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType StateType;
            public ComponentType ChangedType;

            public DataBufferReader  Buffer;
            public SnapshotSender    Sender;
            public StSnapshotRuntime Runtime;
            public int               EntityLength;

            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            [WriteOnly]
            public NativeArray<int> CurrReadDataCursor;

            [WriteOnly]
            public NativeList<AddRequest> AddComponentRequests;

            public EntityCommandBuffer Ecb;

            public void Execute()
            {
                for (var index = 0; index != EntityLength; index++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    var skip        = Buffer.ReadValue<StreamerSkipReason>();

                    if (skip != StreamerSkipReason.NoSkip)
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        if (skip == StreamerSkipReason.NoComponent
                            && States.Exists(worldEntity))
                        {
                            Ecb.RemoveComponent(worldEntity, StateType);
                            // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                            if (Changes.Exists(worldEntity))
                            {
                                Ecb.RemoveComponent(worldEntity, ChangedType);
                            }
                        }

                        continue; // skip
                    }

                    var newData = default(TState);
                    newData.Read(ref Buffer, Sender, Runtime);

                    if (!States.Exists(worldEntity))
                    {
                        AddComponentRequests.Add(new AddRequest {entity = worldEntity, data = newData});
                        continue;
                    }

                    States[worldEntity] = newData;
                }

                CurrReadDataCursor[0] = Buffer.CurrReadIndex;
            }
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);
            UpdateComponentDataFromEntity();

            new WriteJob
            {
                Buffer       = buffer,
                Changes      = Changed,
                States       = States,
                EntityLength = entityLength,
                Receiver     = receiver,
                Runtime      = runtime
            }.Run();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            using (var readCursor = new NativeArray<int>(1, Allocator.TempJob) {[0] = sysData.CurrReadIndex})
            using (var addRequest = new NativeList<AddRequest>(length, Allocator.TempJob))
            {
                new ReadJob
                {
                    Buffer               = sysData,
                    CurrReadDataCursor   = readCursor,
                    Changes              = Changed,
                    States               = States,
                    ChangedType          = ComponentType.Create<DataChanged<TState>>(),
                    StateType            = ComponentType.Create<TState>(),
                    EntityLength         = length,
                    Sender               = sender,
                    Runtime              = runtime,
                    AddComponentRequests = addRequest,

                    Ecb = ecb
                }.Run();

                sysData.CurrReadIndex = readCursor[0];

                for (var i = 0; i != addRequest.Length; i++)
                {
                    ecb.AddComponent(addRequest[i].entity, addRequest[i].data);
                }

                ecb.Playback(EntityManager);
            }
        }
    }

    public abstract class SnapshotEntityComponentStatusStreamer<TComponent> : SnapshotEntityDataStreamerBufferBase<TComponent>
        where TComponent : struct, IBufferElementData
    {
        [BurstCompile]
        private struct WriteJob : IJob
        {
            public DataBufferWriter  Buffer;
            public SnapshotReceiver  Receiver;
            public StSnapshotRuntime Runtime;
            public int               EntityLength;

            [ReadOnly]
            public BufferFromEntity<TComponent> Components;

            public void Execute()
            {
                for (var i = 0; i != EntityLength; i++)
                {
                    var entity = Runtime.Entities[i].Source;
                    if (!Components.Exists(entity))
                    {
                        Buffer.WriteUnmanaged(StreamerSkipReason.NoComponent);
                        continue;
                    }

                    Buffer.WriteUnmanaged(StreamerSkipReason.NoSkip);
                }
            }
        }

        private struct AddRequest
        {
            public Entity entity;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType ComponentType;

            public DataBufferReader  Buffer;
            public SnapshotSender    Sender;
            public StSnapshotRuntime Runtime;
            public int               EntityLength;

            [ReadOnly]
            public BufferFromEntity<TComponent> Components;

            [WriteOnly]
            public NativeArray<int> CurrReadDataCursor;

            [WriteOnly]
            public NativeList<AddRequest> AddComponentRequests;

            public EntityCommandBuffer Ecb;

            public void Execute()
            {
                for (var index = 0; index != EntityLength; index++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    var skip        = Buffer.ReadValue<StreamerSkipReason>();

                    if (skip != StreamerSkipReason.NoSkip)
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        if (skip == StreamerSkipReason.NoComponent
                            && Components.Exists(worldEntity))
                        {
                            Ecb.RemoveComponent(worldEntity, ComponentType);
                        }

                        continue; // skip
                    }

                    if (Components.Exists(worldEntity))
                        continue;

                    AddComponentRequests.Add(new AddRequest {entity = worldEntity});
                }

                CurrReadDataCursor[0] = Buffer.CurrReadIndex;
            }
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);

            new WriteJob
            {
                Buffer       = buffer,
                Components   = GetBufferFromEntity<TComponent>(),
                EntityLength = entityLength,
                Receiver     = receiver,
                Runtime      = runtime
            }.Run();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            using (var readCursor = new NativeArray<int>(1, Allocator.TempJob) {[0] = sysData.CurrReadIndex})
            using (var addRequest = new NativeList<AddRequest>(length, Allocator.TempJob))
            {
                new ReadJob
                {
                    Buffer               = sysData,
                    CurrReadDataCursor   = readCursor,
                    Components           = GetBufferFromEntity<TComponent>(),
                    ComponentType        = ComponentType.Create<TComponent>(),
                    EntityLength         = length,
                    Sender               = sender,
                    Runtime              = runtime,
                    AddComponentRequests = addRequest,

                    Ecb = ecb
                }.Run();

                sysData.CurrReadIndex = readCursor[0];

                for (var i = 0; i != addRequest.Length; i++)
                {
                    ecb.AddBuffer<TComponent>(addRequest[i].entity);
                }

                ecb.Playback(EntityManager);
            }
        }
    }
}
using System;
using System.Runtime.CompilerServices;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
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
        internal TWriteEntityPayload m_CurrentWritePayload;
        internal TReadEntityPayload  m_CurrentReadPayload;

        public virtual bool CanBurstWriteJob => true;
        public virtual bool CanBurstReadJob  => true;

        protected abstract void UpdatePayloadW(ref TWriteEntityPayload current);
        protected abstract void UpdatePayloadR(ref TReadEntityPayload  current);

        [BurstCompile]
        struct WriteJob : IJob
        {
            public DataBufferWriter  Buffer;
            public SnapshotReceiver  Receiver;
            public SnapshotRuntime Runtime;
            public int               EntityLength;

            public int StateTypeIndex;

            public byte IsSameWriteType;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excludeds;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blockeds;

            [ReadOnly]
            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            [NativeDisableUnsafePtrRestriction]
            public TWriteEntityPayload cp;

            public void Execute()
            {
                const byte ReasonNoComponent = (byte) StreamerSkipReason.NoComponent;
                const byte ReasonDelta       = (byte) StreamerSkipReason.Delta;
                const byte ReasonNoSkip      = (byte) StreamerSkipReason.NoSkip;

                DataBufferMarker marker = default;

                byte bitMask = 0;
                var  index   = 0;
                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var entity = Runtime.Entities[entityIndex].Source;
                    if (Excludeds.Exists(entity))
                        continue;
                    if (Blockeds.Exists(entity))
                    {
                        var blockedComponents = Blockeds[entity];
                        var ct                = false;
                        for (var i = 0; i != blockedComponents.Length; i++)
                        {
                            if (blockedComponents[i].TypeIdx != StateTypeIndex)
                                continue;

                            ct = true;
                        }

                        if (ct)
                            continue;
                    }

                    // 4 because 8 bits and 2 bits used per flag write
                    var mod = (byte) (index % (sizeof(byte) * 4));
                    if (mod == 0)
                    {
                        bitMask = 0;
                        marker  = Buffer.WriteByte(0);
                    }

                    index++;

                    if (!States.Exists(entity))
                    {
                        MainBit.SetByteRangeAt(ref bitMask, (byte) (mod * 2), (byte) StreamerSkipReason.NoComponent, 2);
                        Buffer.WriteByte(bitMask, marker);
                        continue;
                    }

                    var change = default(DataChanged<TState>);
                    change.IsDirty = true;

                    if (Changes.Exists(entity))
                        change = Changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(Receiver, change))
                    {
                        MainBit.SetByteRangeAt(ref bitMask, (byte) (mod * 2), (byte) StreamerSkipReason.Delta, 2);
                        Buffer.WriteByte(bitMask, marker);
                        continue;
                    }

                    if (IsSameWriteType == 1)
                    {
                        var state = States[entity];
                        cp = Unsafe.As<TState, TWriteEntityPayload>(ref state);
                    }

                    MainBit.SetByteRangeAt(ref bitMask, (byte) (mod * 2), (byte) StreamerSkipReason.NoSkip, 2);
                    Buffer.WriteByte(bitMask, marker);
                    cp.Write(entityIndex, entity, States, Changes, Buffer, Receiver, Runtime);
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

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);
            UpdateComponentDataFromEntity();

            UpdatePayloadW(ref m_CurrentWritePayload);

            Profiler.BeginSample("CallWriteData (Bursted)");
            new WriteJob
            {
                Buffer       = buffer,
                Changes      = Changed,
                States       = States,
                EntityLength = entityLength,
                Receiver     = receiver,
                Runtime      = runtime,
                cp           = m_CurrentWritePayload,

                StateTypeIndex = StateType.TypeIndex,
                Excludeds      = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                Blockeds       = GetBufferFromEntity<BlockComponentSerialization>(),

                IsSameWriteType = Convert.ToByte(typeof(TState) == typeof(TWriteEntityPayload))
            }.Run();
            Profiler.EndSample();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, SnapshotRuntime runtime, ref DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();
            UpdatePayloadR(ref m_CurrentReadPayload);

            byte bitMask = 0;
            var  index   = 0;
            for (var entityIndex = 0; entityIndex != length; entityIndex++)
            {
                var worldEntity = runtime.GetWorldEntityFromGlobal(entityIndex);
                if (EntityManager.HasComponent<ExcludeFromDataStreamer>(worldEntity))
                    continue;
                if (EntityManager.HasComponent<BlockComponentSerialization>(worldEntity))
                {
                    var blockedComponents = EntityManager.GetBuffer<BlockComponentSerialization>(worldEntity);
                    var ct                = false;
                    for (var i = 0; i != blockedComponents.Length; i++)
                    {
                        if (blockedComponents[i].TypeIdx != StateType.TypeIndex)
                            continue;

                        ct = true;
                    }

                    if (ct)
                        continue;
                }

                // 4 because 8 bits and 2 bits used per flag write
                var mod = (byte) (index % (sizeof(byte) * 4));
                if (mod == 0)
                {
                    bitMask = sysData.ReadValue<byte>();
                }

                index++;

                var skip = (StreamerSkipReason) MainBit.GetByteRangeAt(bitMask, (byte) (mod * 2), 2);

                if (skip != StreamerSkipReason.NoSkip)
                {
                    // If the component don't exist in the snapshot, also remove it from our world.
                    if (skip == StreamerSkipReason.NoComponent
                        && StateExists(worldEntity))
                    {
                        EntityManager.RemoveComponent<TState>(worldEntity);
                        UpdateComponentDataFromEntity();
                        
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

                m_CurrentReadPayload.Read(entityIndex, worldEntity, States, ref sysData, sender, runtime);
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
}
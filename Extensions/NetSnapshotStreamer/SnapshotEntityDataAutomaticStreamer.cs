using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace StormiumShared.Core.Networking
{
    public abstract class SnapshotEntityDataAutomaticStreamer<TState> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData
    {
        [BurstCompile]
        private struct WriteJob : IJob
        {
            public DataBufferWriter Buffer;
            public SnapshotReceiver Receiver;
            public SnapshotRuntime  Runtime;
            public int              EntityLength;
            public int              StateTypeIndex;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excludeds;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blockeds;

            [ReadOnly]
            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            public void Execute()
            {
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
                    change.IsDirty = 1;

                    if (Changes.Exists(entity))
                        change = Changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(Receiver, change))
                    {
                        MainBit.SetByteRangeAt(ref bitMask, (byte) (mod * 2), (byte) StreamerSkipReason.Delta, 2);
                        Buffer.WriteByte(bitMask, marker);
                        continue;
                    }

                    MainBit.SetByteRangeAt(ref bitMask, (byte) (mod * 2), (byte) StreamerSkipReason.NoSkip, 2);
                    Buffer.WriteByte(bitMask, marker);
                    Buffer.WriteValue(States[entity]);
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

            public UnsafeAllocation<DataBufferReader> BufferReference;
            public SnapshotSender                     Sender;
            public SnapshotRuntime                    Runtime;
            public int                                EntityLength;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excludeds;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blockeds;

            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            [WriteOnly]
            public NativeList<AddRequest> AddComponentRequests;

            public EntityCommandBuffer Ecb;

            public void Execute()
            {
                ref var buffer = ref BufferReference.AsRef();

                byte bitMask = 0;
                var  index   = 0;
                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    if (Excludeds.Exists(worldEntity))
                        continue;
                    if (Blockeds.Exists(worldEntity))
                    {
                        var blockedComponents = Blockeds[worldEntity];
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
                        bitMask = buffer.ReadValue<byte>();
                    }

                    index++;

                    var skip = (StreamerSkipReason) MainBit.GetByteRangeAt(bitMask, (byte) (mod * 2), 2);
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

                    var newData = buffer.ReadValue<TState>();
                    if (!States.Exists(worldEntity))
                    {
                        AddComponentRequests.Add(new AddRequest {entity = worldEntity, data = newData});
                        continue;
                    }

                    States[worldEntity] = newData;
                }
            }
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime)
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
                Runtime      = runtime,

                StateTypeIndex = StateType.TypeIndex,
                Excludeds      = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                Blockeds       = GetBufferFromEntity<BlockComponentSerialization>()
            }.Run();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, SnapshotRuntime runtime, ref DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            using (var addRequest = new NativeList<AddRequest>(length, Allocator.TempJob))
            {
                new ReadJob
                {
                    BufferReference      = UnsafeAllocation.From(ref sysData),
                    Changes              = Changed,
                    States               = States,
                    ChangedType          = ComponentType.ReadWrite<DataChanged<TState>>(),
                    StateType            = StateType,
                    EntityLength         = length,
                    Sender               = sender,
                    Runtime              = runtime,
                    AddComponentRequests = addRequest,

                    Excludeds = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                    Blockeds  = GetBufferFromEntity<BlockComponentSerialization>(),

                    Ecb = ecb
                }.Run();

                for (var i = 0; i != addRequest.Length; i++)
                {
                    ecb.AddComponent(addRequest[i].entity, addRequest[i].data);
                }

                ecb.Playback(EntityManager);
            }
        }
    }
}
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace StormiumShared.Core.Networking
{
    public abstract class SnapshotEntityDataManualValueTypeStreamer<TState> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData, ISerializableAsPayload
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
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            [ReadOnly]
            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            // We will only write one bit mask (component existence mask)
            private void WriteFull()
            {
                DataBufferMarker missingComponentMaskMarker = default;

                var missingComponentMask = new CustomBitMask();

                var index = 0;

                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var entity = Runtime.Entities[entityIndex].Source;
                    if (Excluded.Exists(entity))
                        continue;
                    if (Blocked.Exists(entity))
                    {
                        var blockedComponents = Blocked[entity];
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

                    var mod = (byte) (index % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        missingComponentMask.Mask  = 0;
                        missingComponentMaskMarker = Buffer.WriteByte(0);
                    }

                    index++;

                    if (!States.Exists(entity))
                    {
                        missingComponentMask[mod] = true;
                        Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                        continue;
                    }

                    Buffer.WriteValue(States[entity]);
                    Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                }
            }

            // We will write two bit mask (component existence mask and delta change mask)
            private void WriteDelta()
            {
                DataBufferMarker missingComponentMaskMarker = default, deltaMaskMarker = default;

                var missingComponentMask = new CustomBitMask();
                var deltaMask            = new CustomBitMask();

                var index         = 0;
                var existingIndex = 0;

                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var entity = Runtime.Entities[entityIndex].Source;
                    if (Excluded.Exists(entity))
                        continue;
                    if (Blocked.Exists(entity))
                    {
                        var blockedComponents = Blocked[entity];
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

                    var mod = (byte) (index % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        missingComponentMask.Mask  = 0;
                        missingComponentMaskMarker = Buffer.WriteByte(0);
                    }

                    index++;

                    if (!States.Exists(entity))
                    {
                        missingComponentMask[mod] = true;
                        if (deltaMaskMarker.Valid)
                            Buffer.WriteByte(deltaMask.Mask, deltaMaskMarker);
                        Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                        continue;
                    }

                    mod = (byte) (existingIndex % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        deltaMask.Mask  = 0;
                        deltaMaskMarker = Buffer.WriteByte(0);
                    }

                    existingIndex++;

                    var change = new DataChanged<TState> {IsDirty = true};
                    if (Changes.Exists(entity))
                        change = Changes[entity];
                    
                    if (!change.IsDirty)
                    {
                        deltaMask[mod] = true;

                        Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                        Buffer.WriteByte(deltaMask.Mask, deltaMaskMarker);
                        continue;
                    }

                    Buffer.WriteByte(deltaMask.Mask, deltaMaskMarker);
                    Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                    Buffer.WriteValue(States[entity]);
                }
            }

            public void Execute()
            {
                var skipDeltaCondition = (Receiver.Flags & SnapshotFlags.FullData) != 0;
                if (skipDeltaCondition)
                {
                    WriteFull();
                }
                else
                {
                    WriteDelta();
                }
            }
        }

        private struct AddRequest
        {
            public Entity Entity;
            public TState Data;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType StateType;
            public ComponentType ChangedType;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            public ComponentDataFromEntity<TState> States;

            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>> Changes;

            public UnsafeAllocation<DataBufferReader> BufferReference;
            public SnapshotSender                     Sender;
            public SnapshotRuntime                    Runtime;
            public int                                EntityLength;

            public EntityCommandBuffer Ecb;

            public NativeList<AddRequest> Requests;

            private void ReadFull()
            {
                ref var buffer = ref BufferReference.AsRef();

                var missingComponentMask = new CustomBitMask();

                var index = 0;
                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    if (Excluded.Exists(worldEntity))
                        continue;
                    if (Blocked.Exists(worldEntity))
                    {
                        var blockedComponents = Blocked[worldEntity];
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
                    var mod = (byte) (index % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        missingComponentMask.Mask = buffer.ReadValue<byte>();
                    }

                    index++;

                    var hasState = States.Exists(worldEntity);
                    if (missingComponentMask[mod]) // skip?
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        if (hasState)
                        {
                            Ecb.RemoveComponent(worldEntity, StateType);
                        }

                        // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                        if (Changes.Exists(worldEntity))
                        {
                            Ecb.RemoveComponent(worldEntity, ChangedType);
                        }

                        continue; // skip
                    }

                    var newData = new TState();
                    newData.Read(ref buffer, Sender, Runtime);

                    if (!hasState)
                    {
                        Requests.Add(new AddRequest {Data = newData, Entity = worldEntity});
                        continue;
                    }

                    States[worldEntity] = newData;
                }
            }

            private void ReadDelta()
            {
                ref var buffer = ref BufferReference.AsRef();

                var missingComponentMask = new CustomBitMask();
                var deltaMask            = new CustomBitMask();

                var index         = 0;
                var existingIndex = 0;

                for (var entityIndex = 0; entityIndex != EntityLength; entityIndex++)
                {
                    var worldEntity = Runtime.GetWorldEntityFromGlobal(index);
                    if (Excluded.Exists(worldEntity))
                        continue;
                    if (Blocked.Exists(worldEntity))
                    {
                        var blockedComponents = Blocked[worldEntity];
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

                    var missingComponentMod = (byte) (index % (sizeof(byte) * 8));
                    if (missingComponentMod == 0)
                    {
                        missingComponentMask.Mask = buffer.ReadValue<byte>();
                    }

                    index++;

                    var hasState = States.Exists(worldEntity);
                    if (missingComponentMask[missingComponentMod]) // skip?
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        if (hasState)
                        {
                            Ecb.RemoveComponent(worldEntity, StateType);
                        }

                        // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                        if (Changes.Exists(worldEntity))
                        {
                            Ecb.RemoveComponent(worldEntity, ChangedType);
                        }

                        continue; // skip
                    }
                    
                    var deltaMod = (byte) (existingIndex % (sizeof(byte) * 8));
                    if (deltaMod == 0)
                    {
                        deltaMask.Mask = buffer.ReadValue<byte>();
                    }

                    existingIndex++;

                    if (deltaMask[deltaMod]) // skip if there are no delta change... (that mean there is already the component on the entity)
                        continue;

                    var newData = new TState();
                    newData.Read(ref buffer, Sender, Runtime);

                    if (!hasState)
                    {
                        Requests.Add(new AddRequest {Entity = worldEntity, Data = newData});
                        continue;
                    }

                    States[worldEntity] = newData;
                }
            }

            public void Execute()
            {
                var skipDeltaCondition = (Sender.Flags & SnapshotFlags.FullData) != 0;
                if (skipDeltaCondition)
                {
                    ReadFull();
                }
                else
                {
                    ReadDelta();
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
                Excluded       = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                Blocked        = GetBufferFromEntity<BlockComponentSerialization>()
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
                    BufferReference = UnsafeAllocation.From(ref sysData),
                    Changes         = Changed,
                    States          = States,
                    ChangedType     = ComponentType.ReadWrite<DataChanged<TState>>(),
                    StateType       = StateType,
                    EntityLength    = length,
                    Sender          = sender,
                    Runtime         = runtime,
                    Requests        = addRequest,

                    Excluded = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                    Blocked  = GetBufferFromEntity<BlockComponentSerialization>(),

                    Ecb = ecb
                }.Run();

                ecb.Playback(EntityManager);

                for (var i = 0; i != addRequest.Length; i++)
                {
                    EntityManager.AddComponentData(addRequest[i].Entity, addRequest[i].Data);
                }
            }
        }
    }
}
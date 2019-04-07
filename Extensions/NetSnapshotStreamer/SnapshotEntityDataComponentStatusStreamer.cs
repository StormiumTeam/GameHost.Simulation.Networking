using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace StormiumShared.Core.Networking
{
    public abstract class SnapshotEntityComponentStatusStreamerBuffer<TComponent> : SnapshotEntityDataStreamerBufferBase<TComponent>
        where TComponent : struct, IBufferElementData
    {
        [BurstCompile]
        private struct WriteJob : IJob
        {
            public DataBufferWriter Buffer;
            public SnapshotRuntime  Runtime;
            public int              EntityLength;

            public int ComponentTypeIndex;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            [ReadOnly]
            public BufferFromEntity<TComponent> Components;

            public void Execute()
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
                            if (blockedComponents[i].TypeIdx != ComponentTypeIndex)
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

                    if (!Components.Exists(entity))
                    {
                        missingComponentMask[mod] = true;
                        Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                    }
                }
            }
        }

        private struct AddRequest
        {
            public Entity Entity;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType ComponentType;

            public SnapshotRuntime Runtime;
            public int             EntityLength;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            [ReadOnly]
            public BufferFromEntity<TComponent> Components;

            public UnsafeAllocation<DataBufferReader> BufferReference;

            public EntityCommandBuffer    Ecb;
            public NativeList<AddRequest> Requests;

            public void Execute()
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
                            if (blockedComponents[i].TypeIdx != ComponentType.TypeIndex)
                                continue;

                            ct = true;
                        }

                        if (ct)
                            continue;
                    }

                    var mod = (byte) (index % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        missingComponentMask.Mask = buffer.ReadValue<byte>();
                    }

                    index++;

                    var hasComponent = Components.Exists(worldEntity);
                    if (missingComponentMask[mod] && hasComponent) // skip?
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        Ecb.RemoveComponent(worldEntity, ComponentType);

                        continue; // skip
                    }

                    if (hasComponent)
                        continue;

                    Requests.Add(new AddRequest {Entity = worldEntity});
                }
            }
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);

            new WriteJob
            {
                Buffer       = buffer,
                Components   = GetBufferFromEntity<TComponent>(),
                EntityLength = entityLength,
                Runtime      = runtime,

                ComponentTypeIndex = ComponentType.ReadWrite<TComponent>().TypeIndex,
                Excluded           = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                Blocked            = GetBufferFromEntity<BlockComponentSerialization>(),
            }.Run();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, SnapshotRuntime runtime, ref DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            using (var addRequest = new NativeList<AddRequest>(length, Allocator.TempJob))
            {
                new ReadJob
                {
                    ComponentType   = ComponentType.ReadWrite<TComponent>(),
                    Components      = GetBufferFromEntity<TComponent>(),
                    EntityLength    = length,
                    Runtime         = runtime,
                    Requests        = addRequest,
                    BufferReference = UnsafeAllocation.From(ref sysData),

                    Excluded = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                    Blocked  = GetBufferFromEntity<BlockComponentSerialization>(),

                    Ecb = ecb
                }.Run();

                for (var i = 0; i != addRequest.Length; i++)
                {
                    ecb.AddBuffer<TComponent>(addRequest[i].Entity);
                }

                ecb.Playback(EntityManager);
            }
        }
    }

    public abstract class SnapshotEntityComponentStatusStreamer<TComponent> : SnapshotEntityDataStreamerBase<TComponent>
        where TComponent : struct, IComponentData
    {
        [BurstCompile]
        private struct WriteJob : IJob
        {
            public DataBufferWriter Buffer;
            public SnapshotRuntime  Runtime;
            public int              EntityLength;

            public int ComponentTypeIndex;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            [ReadOnly]
            public ComponentDataFromEntity<TComponent> Components;

            public void Execute()
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
                            if (blockedComponents[i].TypeIdx != ComponentTypeIndex)
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

                    if (!Components.Exists(entity))
                    {
                        missingComponentMask[mod] = true;
                        Buffer.WriteByte(missingComponentMask.Mask, missingComponentMaskMarker);
                    }
                }
            }
        }

        private struct AddRequest
        {
            public Entity Entity;
        }

        [BurstCompile]
        private struct ReadJob : IJob
        {
            public ComponentType ComponentType;

            public SnapshotRuntime Runtime;
            public int             EntityLength;

            [ReadOnly]
            public ComponentDataFromEntity<ExcludeFromDataStreamer> Excluded;

            [ReadOnly]
            public BufferFromEntity<BlockComponentSerialization> Blocked;

            [ReadOnly]
            public ComponentDataFromEntity<TComponent> Components;

            public UnsafeAllocation<DataBufferReader> BufferReference;

            public EntityCommandBuffer    Ecb;
            public NativeList<AddRequest> Requests;

            public void Execute()
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
                            if (blockedComponents[i].TypeIdx != ComponentType.TypeIndex)
                                continue;

                            ct = true;
                        }

                        if (ct)
                            continue;
                    }

                    var mod = (byte) (index % (sizeof(byte) * 8));
                    if (mod == 0)
                    {
                        missingComponentMask.Mask = buffer.ReadValue<byte>();
                    }

                    index++;

                    var hasComponent = Components.Exists(worldEntity);
                    if (missingComponentMask[mod] && hasComponent) // skip?
                    {
                        // If the component don't exist in the snapshot, also remove it from our world.
                        Ecb.RemoveComponent(worldEntity, ComponentType);

                        continue; // skip
                    }

                    if (hasComponent)
                        continue;

                    Requests.Add(new AddRequest {Entity = worldEntity});
                }
            }
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);

            new WriteJob
            {
                Buffer       = buffer,
                Components   = GetComponentDataFromEntity<TComponent>(),
                EntityLength = entityLength,
                Runtime      = runtime,

                ComponentTypeIndex = ComponentType.ReadWrite<TComponent>().TypeIndex,
                Excluded           = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                Blocked            = GetBufferFromEntity<BlockComponentSerialization>(),
            }.Run();

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, SnapshotRuntime runtime, ref DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);

            using (var ecb = new EntityCommandBuffer(Allocator.TempJob))
            using (var addRequest = new NativeList<AddRequest>(length, Allocator.TempJob))
            {
                new ReadJob
                {
                    Components      = GetComponentDataFromEntity<TComponent>(),
                    ComponentType   = ComponentType.ReadWrite<TComponent>(),
                    EntityLength    = length,
                    Runtime         = runtime,
                    Requests        = addRequest,
                    BufferReference = UnsafeAllocation.From(ref sysData),

                    Excluded = GetComponentDataFromEntity<ExcludeFromDataStreamer>(),
                    Blocked  = GetBufferFromEntity<BlockComponentSerialization>(),

                    Ecb = ecb
                }.Run();

                for (var i = 0; i != addRequest.Length; i++)
                {
                    ecb.AddComponent(addRequest[i].Entity, new TComponent());
                }

                ecb.Playback(EntityManager);
            }
        }
    }
}
using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.NetCode
{
    public struct ReplicatedEntity : IComponentData
    {
    }

    public struct ReplicatedEntitySerializer : IBufferElementData
    {
        public int Index;
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    public class GhostReceiveSystemGroup : ComponentSystemGroup
    {
        private GhostReceiveSystem m_recvSystem;

        protected override void OnCreateManager()
        {
            m_recvSystem = World.GetOrCreateSystem<GhostReceiveSystem>();
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            m_recvSystem.Update();
        }
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateAfter(typeof(GhostReceiveSystemGroup))]
    public class GhostSpawnSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    [UpdateAfter(typeof(GhostSpawnSystemGroup))]
    public class GhostUpdateDataGroup : ComponentSystemGroup
    {
    }

    [AlwaysUpdateSystem]
    [DisableAutoCreation]
    public class GhostReceiveSystem : JobComponentSystem
    {
        private EntityQuery playerGroup;

        public struct GhostEntity
        {
            public Entity entity;
        }

        struct DelayedDespawnGhost
        {
            public Entity ghost;
            public uint   tick;
        }

        public NativeHashMap<int, GhostEntity> GhostEntityMap => m_ghostEntityMap;
        public JobHandle                       Dependency     => m_Dependency;

        private NativeHashMap<int, GhostEntity>          m_ghostEntityMap;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        private NativeQueue<DelayedDespawnGhost> m_DelayedDespawnQueue;
        private JobHandle                        m_Dependency;

        protected override void OnCreateManager()
        {
            m_ghostEntityMap = new NativeHashMap<int, GhostEntity>(2048, Allocator.Persistent);

            playerGroup = GetEntityQuery(
                ComponentType.ReadWrite<NetworkStreamConnection>(),
                ComponentType.ReadOnly<PlayerStateComponentData>(),
                ComponentType.Exclude<NetworkStreamDisconnected>());

            m_Barrier             = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_CompressionModel    = new NetworkCompressionModel(Allocator.Persistent);
            m_DelayedDespawnQueue = new NativeQueue<DelayedDespawnGhost>(Allocator.Persistent);
        }

        private NetworkCompressionModel m_CompressionModel;

        protected override void OnDestroyManager()
        {
            m_CompressionModel.Dispose();
            m_ghostEntityMap.Dispose();
            m_DelayedDespawnQueue.Dispose();
        }

        struct ClearGhostsJob : IJobProcessComponentDataWithEntity<ReplicatedEntity>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;

            public void Execute(Entity entity, int index, [ReadOnly] ref ReplicatedEntity repl)
            {
                commandBuffer.RemoveComponent<ReplicatedEntity>(index, entity);
            }
        }

        struct ClearMapJob : IJob
        {
            public NativeHashMap<int, GhostEntity> ghostMap;

            public void Execute()
            {
                ghostMap.Clear();
            }
        }

        [BurstCompile]
        struct ReadStreamJob : IJob
        {
            public                             EntityCommandBuffer                                         commandBuffer;
            [DeallocateOnJobCompletion] public NativeArray<Entity>                                         players;
            public                             BufferFromEntity<IncomingSnapshotDataStreamBufferComponent> snapshotFromEntity;
            public                             BufferFromEntity<ReplicatedEntitySerializer>                serializerFromEntity;
            public                             ComponentDataFromEntity<NetworkSnapshotAck>                 snapshotAckFromEntity;

            [NativeDisableContainerSafetyRestriction]
            public NativeHashMap<int, GhostEntity> ghostEntityMap;

            public                             NetworkCompressionModel               compressionModel;
            [DeallocateOnJobCompletion] public NativeArray<GhostSerializerReference> serializers;
            public                             ComponentType                         replicatedEntityType;
            public                             NativeQueue<DelayedDespawnGhost>      delayedDespawnQueue;

            [NativeDisableContainerSafetyRestriction]
            public NativeList<NewGhost> spawnGhosts;

            public uint targetTick;

            public unsafe void Execute()
            {
                // FIXME: should handle any number of connections with individual ghost mappings for each
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                if (players.Length > 1)
                    throw new InvalidOperationException("Ghost receive system only supports a single connection");
#endif
                while (delayedDespawnQueue.Count > 0 &&
                       !SequenceHelpers.IsNewer(delayedDespawnQueue.Peek().tick, targetTick))
                {
                    commandBuffer.RemoveComponent(delayedDespawnQueue.Dequeue().ghost, replicatedEntityType);
                }

                var snapshot = snapshotFromEntity[players[0]];
                if (snapshot.Length == 0)
                    return;

                var dataStream =
                    DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) snapshot.GetUnsafePtr(), snapshot.Length);
                // Read the ghost stream
                // find entities to spawn or destroy
                var readCtx    = new DataStreamReader.Context();
                var serverTick = dataStream.ReadUInt(ref readCtx);
                var ack        = snapshotAckFromEntity[players[0]];
                if (ack.LastReceivedSnapshotByLocal != 0 && !SequenceHelpers.IsNewer(serverTick, ack.LastReceivedSnapshotByLocal))
                    return;
                if (ack.LastReceivedSnapshotByLocal != 0)
                    ack.ReceivedSnapshotByLocalMask <<= (int) (serverTick - ack.LastReceivedSnapshotByLocal);
                ack.ReceivedSnapshotByLocalMask   |= 1;
                ack.LastReceivedSnapshotByLocal   =  serverTick;
                snapshotAckFromEntity[players[0]] =  ack;

                uint despawnLen = dataStream.ReadUInt(ref readCtx);
                uint updateLen  = dataStream.ReadUInt(ref readCtx);

                for (var i = 0; i < despawnLen; ++i)
                {
                    int         ghostId = (int) dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    GhostEntity ent;
                    if (!ghostEntityMap.TryGetValue(ghostId, out ent))
                        continue;

                    ghostEntityMap.Remove(ghostId);
                    delayedDespawnQueue.Enqueue(new DelayedDespawnGhost {ghost = ent.entity, tick = serverTick});
                }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle bufferSafetyHandle = AtomicSafetyHandle.Create();
#endif

                uint targetArch    = 0;
                uint targetArchLen = 0;
                uint baselineTick  = 0;
                uint baselineTick2 = 0;
                uint baselineTick3 = 0;
                uint baselineLen   = 0;
                int  newGhosts     = 0;
                for (var i = 0; i < updateLen; ++i)
                {
                    if (targetArchLen == 0)
                    {
                        targetArch    = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                        targetArchLen = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    }

                    --targetArchLen;

                    if (baselineLen == 0)
                    {
                        baselineTick  = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                        baselineTick2 = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                        baselineTick3 = serverTick - dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                        baselineLen   = dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    }

                    --baselineLen;

                    int         ghostId = (int) dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    GhostEntity gent;
                    if (!ghostEntityMap.TryGetValue(ghostId, out gent) && !spawnGhosts.Contains(new NewGhost {id = ghostId}))
                    {
                        ++newGhosts;

                        spawnGhosts.Add(new NewGhost
                        {
                            id   = ghostId,
                            tick = serverTick
                        });
                    }

                    var serializerBaseData  = serializers[(int) targetArch];
                    var ptrCompressionModel = UnsafeUtility.AddressOf(ref compressionModel);

                    if (gent.entity != default && serializerFromEntity[gent.entity].HasSerializer(targetArch))
                    {
                        serializerBaseData.Value->Header.FullDeserializeEntityFunc.Invoke(ref serializerBaseData.AsRef(), gent.entity, serverTick, baselineTick, baselineTick2, baselineTick3,
                            &dataStream, &readCtx,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                            bufferSafetyHandle,
#endif
                            ptrCompressionModel);
                    }
                    else
                    {
                        serializerBaseData.Value->Header.SpawnFunc.Invoke(ref serializerBaseData.AsRef(), ghostId, serverTick, &dataStream, &readCtx, ptrCompressionModel);
                    }
                }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.Release(bufferSafetyHandle);
#endif

                while (ghostEntityMap.Capacity < ghostEntityMap.Length + newGhosts)
                    ghostEntityMap.Capacity += 1024;

                for (var i = 0; i != serializers.Length; i++)
                {
                    serializers[i].Dispose();
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var commandBuffer = m_Barrier.CreateCommandBuffer();
            if (playerGroup.IsEmptyIgnoreFilter)
            {
                m_DelayedDespawnQueue.Clear();
                var clearMapJob = new ClearMapJob
                {
                    ghostMap = m_ghostEntityMap
                };
                var clearHandle = clearMapJob.Schedule(inputDeps);
                var clearJob = new ClearGhostsJob
                {
                    commandBuffer = commandBuffer.ToConcurrent()
                };
                inputDeps = clearJob.Schedule(this, inputDeps);
                m_Barrier.AddJobHandleForProducer(inputDeps);
                return JobHandle.CombineDependencies(inputDeps, clearHandle);
            }

            var serializers = World.GetExistingSystem<GhostSerializerCollectionSystem>().BeginDeserialize(this, Allocator.TempJob);

            JobHandle playerHandle;
            var readJob = new ReadStreamJob
            {
                commandBuffer         = commandBuffer,
                players               = playerGroup.ToEntityArray(Allocator.TempJob, out playerHandle),
                snapshotFromEntity    = GetBufferFromEntity<IncomingSnapshotDataStreamBufferComponent>(),
                snapshotAckFromEntity = GetComponentDataFromEntity<NetworkSnapshotAck>(),
                ghostEntityMap        = m_ghostEntityMap,
                compressionModel      = m_CompressionModel,
                serializers           = serializers,

                replicatedEntityType = ComponentType.ReadWrite<ReplicatedEntity>(),
                delayedDespawnQueue  = m_DelayedDespawnQueue,
                spawnGhosts          = World.GetExistingSystem<GhostSpawnSystem>().NewGhostIds,
                serializerFromEntity = GetBufferFromEntity<ReplicatedEntitySerializer>(),
                targetTick           = NetworkTimeSystem.interpolateTargetTick
            };
            inputDeps = readJob.Schedule(JobHandle.CombineDependencies(inputDeps, World.GetExistingSystem<GhostSpawnSystem>().Dependency, playerHandle));

            m_Dependency = inputDeps;
            m_Barrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }
    }
}
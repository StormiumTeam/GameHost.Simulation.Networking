using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace StormiumTeam.Networking
{
    public class StaticGhostCollectionSystem<TCollection> : BaseGhostCollectionSystem
        where TCollection : struct, IGhostCollection
    {
        private TCollection m_Collection;
        
        public StaticGhostCollectionSystem(World world)
        {
            Debug.Log("Initialized Static collection: " + typeof(TCollection));
            m_Collection = new TCollection();
            m_Collection.Initialize(world);
        }
        
        #region Send

        // Not burst compiled due to commandBuffer.SetComponent
        private struct SpawnGhostJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>           spawnChunks;
            public            NativeList<GhostSendSystem.PrioChunk> serialSpawnChunks;
            [ReadOnly] public ArchetypeChunkEntityType              entityType;
            public            TCollection                           serializers;
            public            NativeQueue<int>                      freeGhostIds;
            public            NativeArray<int>                      allocatedGhostIds;
            public            EntityCommandBuffer                   commandBuffer;

            public unsafe void Execute()
            {
                for (int chunk = 0; chunk < spawnChunks.Length; ++chunk)
                {
                    var entities = spawnChunks[chunk].GetNativeArray(entityType);
                    var ghostState = (GhostSendSystem.GhostSystemStateComponent*) UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<GhostSendSystem.GhostSystemStateComponent>() * entities.Length,
                        UnsafeUtility.AlignOf<GhostSendSystem.GhostSystemStateComponent>(), Allocator.TempJob);

                    for (var i = 0; i != serializers.Length; i++)
                    {
                        if (!serializers.CanSerialize(i, spawnChunks[chunk].Archetype))
                            continue;

                        var pc = new GhostSendSystem.PrioChunk
                        {
                            chunk      = spawnChunks[chunk],
                            ghostState = ghostState,
                            priority   = serializers.CalculateImportance(i, spawnChunks[chunk]),
                            startIndex = 0,
                            ghostType  = i
                        };
                        serialSpawnChunks.Add(pc);
                    }

                    for (var ent = 0; ent < entities.Length; ++ent)
                    {
                        int newId;
                        if (!freeGhostIds.TryDequeue(out newId))
                        {
                            newId                = allocatedGhostIds[0];
                            allocatedGhostIds[0] = newId + 1;
                        }

                        ghostState[ent] = new GhostSendSystem.GhostSystemStateComponent {ghostId = newId, despawnTick = 0};

                        // This runs after simulation. If an entity is created in the begin barrier and destroyed before this
                        // runs there can be errors. To get around those we add the ghost system state component before everything
                        // using the begin barrier, and set the value here
                        commandBuffer.SetComponent(entities[ent], ghostState[ent]);
                    }
                }
            }
        }

        [BurstCompile]
        private unsafe struct SerializeJob : IJob
        {
            public UdpNetworkDriver.Concurrent driver;
            public NetworkPipeline unreliablePipeline;
            
            [ReadOnly] public NativeArray<ArchetypeChunk> despawnChunks;
            [ReadOnly] public NativeArray<ArchetypeChunk> ghostChunks;

            public            Entity                                                                connectionEntity;
            public            NativeHashMap<ArchetypeChunk, GhostSendSystem.SerializationStateList> chunkSerializationData;
            [ReadOnly] public ComponentDataFromEntity<NetworkSnapshotAckComponent>                           ackFromEntity;
            [ReadOnly] public ComponentDataFromEntity<NetworkStreamConnection> connectionFromEntity;

            [ReadOnly] public NativeList<GhostSendSystem.PrioChunk> serialSpawnChunks;

            [ReadOnly] public ArchetypeChunkEntityType                                               entityType;
            [ReadOnly] public ArchetypeChunkComponentType<GhostSendSystem.GhostSystemStateComponent> ghostSystemStateType;

            [ReadOnly] public TCollection             serializers;
            [ReadOnly] public NetworkCompressionModel compressionModel;


            public uint currentTick;
            public uint localTime;

            public unsafe void Execute()
            {
                var snapshotAck = ackFromEntity[connectionEntity];
                var ackTick     = snapshotAck.LastReceivedSnapshotByRemote;

                DataStreamWriter dataStream = new DataStreamWriter(2048, Allocator.Temp);
                dataStream.Clear();
                dataStream.Write((byte) NetworkStreamProtocol.Snapshot);

                dataStream.Write(localTime);
                dataStream.Write(snapshotAck.LastReceivedRemoteTime - (localTime - snapshotAck.LastReceiveTimestamp));

                dataStream.Write(currentTick);

                int entitySize = UnsafeUtility.SizeOf<Entity>();

                var  despawnLenWriter = dataStream.Write((uint) 0);
                var  updateLenWriter  = dataStream.Write((uint) 0);
                uint despawnLen       = 0;
                // TODO: if not all despawns fit, sort them based on age and maybe time since last send
                // TODO: only resend despawn on nack
                // FIXME: the TargetPacketSize cannot be used since CleanupGhostJob relies on all ghosts being sent every frame
                for (var chunk = 0; chunk < despawnChunks.Length /*&& dataStream.Length < TargetPacketSize*/; ++chunk)
                {
                    var entities = despawnChunks[chunk].GetNativeArray(entityType);
                    var ghosts   = despawnChunks[chunk].GetNativeArray(ghostSystemStateType);
                    for (var ent = 0; ent < entities.Length /*&& dataStream.Length < TargetPacketSize*/; ++ent)
                    {
                        if (ackTick == 0 || SequenceHelpers.IsNewer(ghosts[ent].despawnTick, ackTick))
                        {
                            dataStream.WritePackedUInt((uint) ghosts[ent].ghostId, compressionModel);
                            ++despawnLen;
                        }
                    }
                }

                uint updateLen    = 0;
                var  serialChunks = new NativeList<GhostSendSystem.PrioChunk>(ghostChunks.Length + serialSpawnChunks.Length, Allocator.Temp);
                serialChunks.AddRange(serialSpawnChunks);
                var existingChunks = new NativeHashMap<ArchetypeChunk, int>(ghostChunks.Length, Allocator.Temp);
                int maxCount       = 0;
                for (int chunk = 0; chunk < ghostChunks.Length; ++chunk)
                {
                    var addNew = !chunkSerializationData.TryGetValue(ghostChunks[chunk], out var chunkStateList);

                    // FIXME: should be using chunk sequence number instead of this hack
                    if (!addNew && chunkStateList.Archetype != ghostChunks[chunk].Archetype)
                    {
                        chunkStateList.Dispose();
                        chunkSerializationData.Remove(ghostChunks[chunk]);
                        addNew = true;
                    }

                    if (addNew)
                    {
                        chunkStateList           = new GhostSendSystem.SerializationStateList(Allocator.Persistent, ghostChunks[chunk].Count);
                        chunkStateList.Archetype = ghostChunks[chunk].Archetype;

                        for (var i = 0; i != serializers.Length; i++)
                        {
                            if (!serializers.CanSerialize(i, ghostChunks[chunk].Archetype))
                                continue;

                            var serializerDataSize = serializers.GetSnapshotSize(i);

                            chunkStateList.Add(new GhostSendSystem.SerializationState
                            {
                                lastUpdate = currentTick - 1,
                                startIndex = 0,
                                ghostType  = i,
                                arch       = ghostChunks[chunk].Archetype,

                                snapshotWriteIndex = 0,
                                snapshotData       = (byte*) UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>() * SnapshotHistorySize + SnapshotHistorySize * ghostChunks[chunk].Capacity * (entitySize + serializerDataSize), 16, Allocator.Persistent)
                            });

                            UnsafeUtility.MemClear(chunkStateList[chunkStateList.Length - 1].snapshotData, UnsafeUtility.SizeOf<int>() * SnapshotHistorySize);
                        }

                        chunkSerializationData.TryAdd(ghostChunks[chunk], chunkStateList);
                    }

                    existingChunks.TryAdd(ghostChunks[chunk], 1);
                    // FIXME: only if modified or force sync
                    for (var i = 0; i != chunkStateList.Length; i++)
                    {
                        var pc = new GhostSendSystem.PrioChunk();
                        pc.chunk      = ghostChunks[chunk];
                        pc.ghostState = null;
                        pc.priority   = serializers.CalculateImportance(chunkStateList[i].ghostType, ghostChunks[chunk]) * (int) (currentTick - chunkStateList[i].lastUpdate);
                        pc.startIndex = chunkStateList[i].startIndex;
                        pc.ghostType  = chunkStateList[i].ghostType;

                        serialChunks.Add(pc);
                    }

                    if (ghostChunks[chunk].Count > maxCount)
                        maxCount = ghostChunks[chunk].Count;
                }

                var oldChunks = chunkSerializationData.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < oldChunks.Length; ++i)
                {
                    int val;
                    if (!existingChunks.TryGetValue(oldChunks[i], out val))
                    {
                        chunkSerializationData.TryGetValue(oldChunks[i], out var chunkStateList);
                        chunkStateList.Dispose();
                        chunkSerializationData.Remove(oldChunks[i]);
                    }
                }

                NativeArray<GhostSendSystem.PrioChunk> serialChunkArray = serialChunks;
                serialChunkArray.Sort();
                var availableBaselines = new NativeList<GhostSendSystem.SnapshotBaseline>(SnapshotHistorySize, Allocator.Temp);
                var baselinePerEntity  = new NativeArray<int>(maxCount * 3, Allocator.Temp);
                for (int pc = 0; pc < serialChunks.Length && dataStream.Length < TargetPacketSize; ++pc) // serialChunks can have the same chunks present
                {
                    var chunk     = serialChunks[pc].chunk;
                    var ghostType = serialChunks[pc].ghostType;

                    Entity* currentSnapshotEntity = null;
                    byte*   currentSnapshotData   = null;
                    availableBaselines.Clear();
                    if (chunkSerializationData.TryGetValue(chunk, out var chunkStateList))
                    {
                        for (var i = 0; i != chunkStateList.Length; i++)
                        {
                            var chunkState = chunkStateList[i];
                            var dataSize   = serializers.GetSnapshotSize(chunkState.ghostType);

                            uint* snapshotIndex = (uint*) chunkState.snapshotData;
                            snapshotIndex[chunkState.snapshotWriteIndex] = currentTick;
                            int baseline = (SnapshotHistorySize + chunkState.snapshotWriteIndex - 1) % SnapshotHistorySize;
                            while (baseline != chunkState.snapshotWriteIndex)
                            {
                                if (snapshotAck.IsReceivedByRemote(snapshotIndex[baseline]))
                                {
                                    byte* dataBase = chunkState.snapshotData +
                                                     UnsafeUtility.SizeOf<int>() * SnapshotHistorySize +
                                                     baseline * (dataSize + entitySize) * chunk.Capacity;
                                    availableBaselines.Add(new GhostSendSystem.SnapshotBaseline
                                    {
                                        tick     = snapshotIndex[baseline],
                                        snapshot = dataBase + entitySize * chunk.Capacity,
                                        entity   = (Entity*) (dataBase)
                                    });
                                }

                                baseline = (SnapshotHistorySize + baseline - 1) % SnapshotHistorySize;
                            }

                            // Find the acked snapshot to delta against, setup pointer to current and previous entity* and data*
                            // Remember to bump writeIndex when done
                            currentSnapshotData   =  chunkState.snapshotData + UnsafeUtility.SizeOf<int>() * SnapshotHistorySize;
                            currentSnapshotData   += chunkState.snapshotWriteIndex * (dataSize + entitySize) * chunk.Capacity;
                            currentSnapshotEntity =  (Entity*) currentSnapshotData;
                            currentSnapshotData   += entitySize * chunk.Capacity;
                        }
                    }

                    var ghosts = serialChunks[pc].ghostState;
                    if (ghosts == null)
                    {
                        ghosts = (GhostSendSystem.GhostSystemStateComponent*) chunk.GetNativeArray(ghostSystemStateType).GetUnsafeReadOnlyPtr();
                    }

                    var ghostEntities = chunk.GetNativeArray(entityType);
                    int ent;
                    if (serialChunks[pc].startIndex < chunk.Count)
                    {
                        dataStream.WritePackedUInt((uint) ghostType, compressionModel);
                        dataStream.WritePackedUInt((uint) (chunk.Count - serialChunks[pc].startIndex), compressionModel);
                    }

                    // First figure out the baselines to use per entity so they can be sent as baseline + maxCount instead of one per entity
                    int targetBaselines = serializers.WantsPredictionDelta(ghostType) ? 3 : 1;
                    for (ent = serialChunks[pc].startIndex; ent < chunk.Count; ++ent)
                    {
                        int foundBaselines = 0;
                        for (int baseline = 0; baseline < availableBaselines.Length; ++baseline)
                        {
                            if (availableBaselines[baseline].entity[ent] == ghostEntities[ent])
                            {
                                baselinePerEntity[ent * 3 + foundBaselines] = baseline;
                                ++foundBaselines;
                                if (foundBaselines == targetBaselines)
                                    break;
                            }
                            // Only way an entity can be missing from a snapshot but be available in an older is if last snapshot was partial
                            else if (availableBaselines[baseline].entity[ent] != Entity.Null)
                                break;
                        }

                        if (foundBaselines == 2)
                            foundBaselines = 1;
                        while (foundBaselines < 3)
                        {
                            baselinePerEntity[ent * 3 + foundBaselines] = -1;
                            ++foundBaselines;
                        }
                    }

                    var invokeData = new GhostSendSystem.InvokeData
                    {
                        serializer            = ghostType,
                        chunk                 = chunk,
                        startIndex            = serialChunks[pc].startIndex,
                        currentTick           = currentTick,
                        currentSnapshotEntity = currentSnapshotEntity,
                        currentSnapshotData   = currentSnapshotData,
                        ghosts                = ghosts,
                        ghostEntities         = ghostEntities,
                        baselinePerEntity     = baselinePerEntity,
                        availableBaselines    = availableBaselines,
                        dataStream            = dataStream,
                        compressionModel      = compressionModel
                    };

                    ent = serializers.Serialize(invokeData);

                    updateLen += (uint) (ent - serialChunks[pc].startIndex);

                    // Spawn chunks are temporary and should not be added to the state data cache
                    for (var i = 0; i != chunkStateList.Length; i++)
                    {
                        var chunkState = chunkStateList[i];
                        if (serialChunks[pc].ghostState == null)
                        {
                            // Only append chunks which contain data
                            if (ent > serialChunks[pc].startIndex)
                            {
                                if (serialChunks[pc].startIndex > 0)
                                    UnsafeUtility.MemClear(currentSnapshotEntity, entitySize * serialChunks[pc].startIndex);
                                if (ent < chunk.Capacity)
                                    UnsafeUtility.MemClear(currentSnapshotEntity + ent, entitySize * (chunk.Capacity - ent));
                                chunkState.snapshotWriteIndex = (chunkState.snapshotWriteIndex + 1) % SnapshotHistorySize;
                            }

                            if (ent >= chunk.Count)
                            {
                                chunkState.lastUpdate = currentTick;
                                chunkState.startIndex = 0;
                            }
                            else
                            {
                                // TODO: should this always be run or should partial chunks only be allowed for the highest priority chunk?
                                //if (pc == 0)
                                chunkState.startIndex = ent;
                            }
                        }

                        chunkStateList[i] = chunkState;
                    }

                    chunkSerializationData.Remove(chunk);
                    chunkSerializationData.TryAdd(chunk, chunkStateList);
                }

                dataStream.Flush();
                despawnLenWriter.Update(despawnLen);
                updateLenWriter.Update(updateLen);

                driver.Send(unreliablePipeline, connectionFromEntity[connectionEntity].Value, dataStream);
            }
        }

        [BurstCompile]
        struct CleanupJob : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           despawnChunks;
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           spawnChunks;
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           ghostChunks;
            public                             NativeList<GhostSendSystem.PrioChunk> serialSpawnChunks;

            public unsafe void Execute()
            {
                for (int i = 0; i < serialSpawnChunks.Length; ++i)
                {
                    UnsafeUtility.Free(serialSpawnChunks[i].ghostState, Allocator.TempJob);
                }
            }
        }

        public override JobHandle ExecuteSend(GhostSendSystem                           sendSystem,
                                              List<GhostSendSystem.ConnectionStateData> connectionStates,
                                              EntityQuery                               ghostSpawnGroup, EntityQuery ghostDespawnGroup, EntityQuery ghostGroup,
                                              NativeList<GhostSendSystem.PrioChunk>     serialSpawnChunks,
                                              NativeQueue<int>                          freeGhostIds, NativeArray<int> allocatedGhostIds,
                                              EntityCommandBuffer                       commandBuffer,
                                              BeginSimulationEntityCommandBufferSystem  barrier,
                                              NetworkCompressionModel                   compressionModel,
                                              uint                                      currentTick,
                                              JobHandle                                 inputDeps)
        {
            var entityType           = sendSystem.GetArchetypeChunkEntityType();
            var ghostSystemStateType = sendSystem.GetArchetypeChunkComponentType<GhostSendSystem.GhostSystemStateComponent>();

            m_Collection.BeginSerialize(sendSystem);

            // Extract all newly spawned ghosts and set their ghost ids
            JobHandle spawnChunkHandle;
            var       spawnChunks = ghostSpawnGroup.CreateArchetypeChunkArray(Allocator.TempJob, out spawnChunkHandle);
            var spawnJob = new SpawnGhostJob
            {
                spawnChunks       = spawnChunks,
                serialSpawnChunks = serialSpawnChunks,
                entityType        = entityType,
                serializers       = m_Collection,
                freeGhostIds      = freeGhostIds,
                allocatedGhostIds = allocatedGhostIds,
                commandBuffer     = commandBuffer
            };
            inputDeps = spawnJob.Schedule(JobHandle.CombineDependencies(inputDeps, spawnChunkHandle));
            // This was the last job using the commandBuffer
            barrier.AddJobHandleForProducer(inputDeps);

            JobHandle despawnChunksHandle, ghostChunksHandle;
            var       despawnChunks = ghostDespawnGroup.CreateArchetypeChunkArray(Allocator.TempJob, out despawnChunksHandle);
            var       ghostChunks   = ghostGroup.CreateArchetypeChunkArray(Allocator.TempJob, out ghostChunksHandle);
            inputDeps = JobHandle.CombineDependencies(inputDeps, despawnChunksHandle, ghostChunksHandle);
            
            var receiveSystem = sendSystem.World.GetExistingSystem<NetworkStreamReceiveSystem>();

            var serialDep = new NativeArray<JobHandle>(connectionStates.Count + 1, Allocator.Temp);
            // In case there are 0 connections
            serialDep[0] = inputDeps;
            for (int con = 0; con < connectionStates.Count; ++con)
            {
                var connectionEntity       = connectionStates[con].Entity;
                var chunkSerializationData = connectionStates[con].SerializationState;
                var serializeJob = new SerializeJob
                {
                    driver             = receiveSystem.ConcurrentDriver,
                    unreliablePipeline = receiveSystem.UnreliablePipeline,
                    
                    despawnChunks          = despawnChunks,
                    ghostChunks            = ghostChunks,
                    connectionEntity       = connectionEntity,
                    chunkSerializationData = chunkSerializationData,
                    ackFromEntity          = sendSystem.GetComponentDataFromEntity<NetworkSnapshotAckComponent>(true),
                    connectionFromEntity   = sendSystem.GetComponentDataFromEntity<NetworkStreamConnection>(true),
                    serialSpawnChunks      = serialSpawnChunks,
                    entityType             = entityType,
                    ghostSystemStateType   = ghostSystemStateType,
                    serializers            = m_Collection,
                    compressionModel       = compressionModel,
                    currentTick            = currentTick,
                    localTime              = NetworkTimeSystem.TimestampMS
                };
                // FIXME: disable safety for BufferFromEntity is not working
                serialDep[con + 1] = serializeJob.Schedule(serialDep[con]);
            }

            inputDeps = JobHandle.CombineDependencies(serialDep);

            var cleanupJob = new CleanupJob
            {
                despawnChunks     = despawnChunks,
                spawnChunks       = spawnChunks,
                ghostChunks       = ghostChunks,
                serialSpawnChunks = serialSpawnChunks,
            };
            return cleanupJob.Schedule(inputDeps);
        }

        #endregion

        #region Receive

        [BurstCompile]
        struct ReadStreamJob : IJob
        {
            public                             EntityCommandBuffer                                         commandBuffer;
            [DeallocateOnJobCompletion] public NativeArray<Entity>                                         players;
            public                             BufferFromEntity<IncomingSnapshotDataStreamBufferComponent> snapshotFromEntity;
            public ComponentDataFromEntity<NetworkSnapshotAckComponent> snapshotAckFromEntity;
            public                             BufferFromEntity<ReplicatedEntitySerializer>                serializerFromEntity;

            [NativeDisableContainerSafetyRestriction]
            public NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostEntityMap;

            public NetworkCompressionModel                             compressionModel;
            public TCollection                                         serializers;
            public ComponentType                                       replicatedEntityType;
            public NativeQueue<GhostReceiveSystem.DelayedDespawnGhost> delayedDespawnQueue;

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
                    int                            ghostId = (int) dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    GhostReceiveSystem.GhostEntity ent;
                    if (!ghostEntityMap.TryGetValue(ghostId, out ent))
                        continue;

                    ghostEntityMap.Remove(ghostId);
                    delayedDespawnQueue.Enqueue(new GhostReceiveSystem.DelayedDespawnGhost {ghost = ent.entity, tick = serverTick});
                }

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

                    int                            ghostId = (int) dataStream.ReadPackedUInt(ref readCtx, compressionModel);
                    GhostReceiveSystem.GhostEntity gent;
                    if (!ghostEntityMap.TryGetValue(ghostId, out gent) && !spawnGhosts.Contains(new NewGhost {id = ghostId}))
                    {
                        ++newGhosts;

                        spawnGhosts.Add(new NewGhost
                        {
                            id   = ghostId,
                            tick = serverTick
                        });
                    }

                    if (gent.entity != default && serializerFromEntity[gent.entity].HasSerializer(targetArch))
                    {
                        var invokeData = new GhostReceiveSystem.InvokeDeserializeData
                        {
                            serializer       = (int) targetArch,
                            entity           = gent.entity,
                            snapshot         = serverTick,
                            baseline         = baselineTick,
                            baseline2        = baselineTick2,
                            baseline3        = baselineTick3,
                            reader           = dataStream,
                            context          = (DataStreamReader.Context*) UnsafeUtility.AddressOf(ref readCtx),
                            compressionModel = compressionModel
                        };

                        serializers.Deserialize(invokeData);
                    }
                    else
                    {
                        var invokeData = new GhostReceiveSystem.InvokeSpawnData
                        {
                            serializer       = (int) targetArch,
                            ghostId          = ghostId,
                            snapshot         = serverTick,
                            reader           = dataStream,
                            context          = &readCtx,
                            compressionModel = compressionModel
                        };

                        serializers.Spawn(invokeData);
                    }
                }

                while (ghostEntityMap.Capacity < ghostEntityMap.Length + newGhosts)
                    ghostEntityMap.Capacity += 1024;
            }
        }

        public override JobHandle ExecuteReceive(GhostReceiveSystem                                  receiveSystem,
                                                 EntityQuery                                         playerGroup,
                                                 NativeHashMap<int, GhostReceiveSystem.GhostEntity>  ghostEntityMap,
                                                 NativeQueue<GhostReceiveSystem.DelayedDespawnGhost> delayedDespawnQueue,
                                                 NetworkCompressionModel                             compressionModel,
                                                 EntityCommandBuffer                                 commandBuffer,
                                                 BeginSimulationEntityCommandBufferSystem            barrier,
                                                 ref JobHandle                                       dependency,
                                                 JobHandle                                           inputDeps)
        {
            m_Collection.BeginDeserialize(receiveSystem);

            JobHandle playerHandle;
            var readJob = new ReadStreamJob
            {
                commandBuffer         = commandBuffer,
                players               = playerGroup.ToEntityArray(Allocator.TempJob, out playerHandle),
                snapshotFromEntity    = receiveSystem.GetBufferFromEntity<IncomingSnapshotDataStreamBufferComponent>(),
                snapshotAckFromEntity = receiveSystem.GetComponentDataFromEntity<NetworkSnapshotAckComponent>(),
                ghostEntityMap        = ghostEntityMap,
                compressionModel      = compressionModel,
                serializers           = m_Collection,

                replicatedEntityType = ComponentType.ReadWrite<ReplicatedEntity>(),
                delayedDespawnQueue  = delayedDespawnQueue,
                spawnGhosts          = receiveSystem.World.GetExistingSystem<GhostSpawnSystem>().NewGhostIds,
                serializerFromEntity = receiveSystem.GetBufferFromEntity<ReplicatedEntitySerializer>(),
                targetTick           = NetworkTimeSystem.interpolateTargetTick
            };
            inputDeps = readJob.Schedule(JobHandle.CombineDependencies(inputDeps, playerHandle));

            dependency = inputDeps;
            barrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }

        #endregion
    }
}
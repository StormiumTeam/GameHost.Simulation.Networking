using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.NetCode
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(NetworkStreamSendSystem))]
    [AlwaysUpdateSystem]
    public class GhostSendSystem : JobComponentSystem
    {
        private DataStreamWriter m_DataStream;

        public struct GhostSystemStateComponent : ISystemStateComponentData
        {
            public int  ghostId;
            public uint despawnTick;
        }

        unsafe struct SerializationStateList : IDisposable
        {
            private struct Data
            {
                public void* Buffer;
                public int   Length;
                public int   Capacity;
            }

            public EntityArchetype Archetype;

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
                    var capacity = math.max(value, 8);
                    var newBuffer = (byte*) UnsafeUtility.Malloc(capacity * UnsafeUtility.SizeOf<SerializationState>(), UnsafeUtility.AlignOf<SerializationState>(), m_Allocator);

                    UnsafeUtility.MemCpy(newBuffer, m_Data->Buffer, m_Data->Length * UnsafeUtility.SizeOf<SerializationState>());
                    UnsafeUtility.Free(m_Data->Buffer, m_Allocator);

                    m_Data->Capacity = capacity;
                    m_Data->Buffer   = newBuffer;
                }
            }

            public SerializationState this[int index]
            {
                get => UnsafeUtility.ReadArrayElement<SerializationState>(m_Data->Buffer, index);
                set => UnsafeUtility.WriteArrayElement<SerializationState>(m_Data->Buffer, index, value);
            }

            private Data*     m_Data;
            private Allocator m_Allocator;

            public SerializationStateList(Allocator allocator, int size)
            {
                m_Allocator = allocator;
                m_Data      = (Data*) UnsafeUtility.Malloc(UnsafeUtility.SizeOf<Data>(), UnsafeUtility.AlignOf<Data>(), m_Allocator);

                var capacity  = math.max(size, 8);
                var newBuffer = (byte*) UnsafeUtility.Malloc(capacity * UnsafeUtility.SizeOf<SerializationStateList>(), UnsafeUtility.AlignOf<byte>(), m_Allocator);

                m_Data->Capacity = capacity;
                m_Data->Buffer   = newBuffer;
                m_Data->Length   = 0;

                Archetype = default;
            }

            public void Add(SerializationState v)
            {
                if (m_Data->Length >= m_Data->Capacity)
                    Capacity = m_Data->Length + m_Data->Capacity * 2;

                this[m_Data->Length++] = v;
            }

            public void RemoveAtSwapBack(int index)
            {
                var newLength = m_Data->Length - 1;
                this[index]    = this[newLength];
                m_Data->Length = newLength;
            }

            public void Dispose()
            {
                var length = Length;
                for (var i = 0; i != length; i++)
                {
                    if (this[i].snapshotData == null)
                    {
                        // Debug.LogError("snapshotData was null before being freed!");
                        continue;
                    }
                    
                    UnsafeUtility.Free(this[i].snapshotData, Allocator.Persistent);
                }
                
                UnsafeUtility.Free(m_Data->Buffer, m_Allocator);
                UnsafeUtility.Free(m_Data, m_Allocator);
            }

            public bool ContainsArchetype(EntityArchetype archetype)
            {
                var length = Length;
                for (var i = 0; i != length; i++)
                {
                    if (archetype == this[i].arch)
                        return true;
                }

                return false;
            }
        }

        unsafe struct SerializationState
        {
            public EntityArchetype arch;
            public uint            lastUpdate;
            public int             startIndex;
            public int             ghostType;

            // the entity and data arrays are 2d arrays (chunk capacity * max snapshots)
            // Find baseline by finding the largest tick not at writeIndex which has been acked by the other end
            // Pass in entity, data [writeIndex] as current and entity, data [baseline] as baseline
            // If entity[baseline] is incorrect there is no delta compression
            public int   snapshotWriteIndex;
            public byte* snapshotData;
        }

        unsafe struct ConnectionStateData : IDisposable
        {
            public unsafe void Dispose()
            {
                var oldChunks = SerializationState.GetKeyArray(Allocator.Temp);
                for (int i = 0; i < oldChunks.Length; ++i)
                {
                    SerializationState.TryGetValue(oldChunks[i], out var stateList);
                    stateList.Dispose();
                }

                SerializationState.Dispose();
            }
            public Entity                                            Entity;
            public NativeHashMap<ArchetypeChunk, SerializationStateList> SerializationState;
        }

        private EntityQuery ghostGroup;
        private EntityQuery ghostSpawnGroup;
        private EntityQuery ghostDespawnGroup;

        private EntityQuery connectionGroup;

        private NativeQueue<int> m_FreeGhostIds;
        private NativeArray<int> m_AllocatedGhostIds;

        private List<ConnectionStateData>  m_ConnectionStates;
        private NativeHashMap<Entity, int> m_ConnectionStateLookup;
        private NetworkCompressionModel    m_CompressionModel;

        private NativeList<PrioChunk> m_SerialSpawnChunks;

        private const int TargetPacketSize    = 1200;
        public const  int SnapshotHistorySize = 32;

        private ServerSimulationSystemGroup              m_ServerSimulation;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreateManager()
        {
            m_DataStream = new DataStreamWriter(2048, Allocator.Persistent);
            ghostGroup   = GetEntityQuery(typeof(GhostComponent), typeof(GhostSystemStateComponent));
            var filterSpawn = new EntityQueryDesc
            {
                All  = new ComponentType[] {typeof(GhostComponent)},
                None = new ComponentType[] {typeof(GhostSystemStateComponent)}
            };
            var filterDespawn = new EntityQueryDesc
            {
                All  = new ComponentType[] {typeof(GhostSystemStateComponent)},
                None = new ComponentType[] {typeof(GhostComponent)}
            };
            ghostSpawnGroup   = GetEntityQuery(filterSpawn);
            ghostDespawnGroup = GetEntityQuery(filterDespawn);

            m_FreeGhostIds         = new NativeQueue<int>(Allocator.Persistent);
            m_AllocatedGhostIds    = new NativeArray<int>(1, Allocator.Persistent);
            m_AllocatedGhostIds[0] = 1; // To make sure 0 is invalid

            connectionGroup = GetEntityQuery(
                ComponentType.ReadWrite<NetworkStreamConnection>(),
                ComponentType.ReadOnly<PlayerStateComponentData>());

            m_ServerSimulation = World.GetExistingSystem<ServerSimulationSystemGroup>();
            m_Barrier          = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();

            m_ConnectionStates      = new List<ConnectionStateData>(256);
            m_ConnectionStateLookup = new NativeHashMap<Entity, int>(256, Allocator.Persistent);
            m_CompressionModel      = new NetworkCompressionModel(Allocator.Persistent);

            m_SerialSpawnChunks = new NativeList<PrioChunk>(1024, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_SerialSpawnChunks.Dispose();
            m_CompressionModel.Dispose();
            m_AllocatedGhostIds.Dispose();
            m_FreeGhostIds.Dispose();
            m_DataStream.Dispose();
            foreach (var connectionState in m_ConnectionStates)
            {
                connectionState.Dispose();
            }

            m_ConnectionStateLookup.Dispose();
        }

        [BurstCompile]
        struct FindAckedByAllJob : IJobProcessComponentData<NetworkSnapshotAck>
        {
            public NativeArray<uint> tick;

            public void Execute([ReadOnly] ref NetworkSnapshotAck ack)
            {
                uint ackedByAllTick = tick[0];
                var  snapshot       = ack.LastReceivedSnapshotByRemote;
                if (snapshot == 0)
                    ackedByAllTick = 0;
                else if (ackedByAllTick != 0 && SequenceHelpers.IsNewer(ackedByAllTick, snapshot))
                    ackedByAllTick = snapshot;
                tick[0] = ackedByAllTick;
            }
        }

        [BurstCompile]
        [ExcludeComponent(typeof(GhostComponent))]
        struct CleanupGhostJob : IJobProcessComponentDataWithEntity<GhostSystemStateComponent>
        {
            public                                        uint                           currentTick;
            [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<uint>              tick;
            public                                        EntityCommandBuffer.Concurrent commandBuffer;
            public                                        NativeQueue<int>.Concurrent    freeGhostIds;
            public                                        ComponentType                  ghostStateType;

            public void Execute(Entity entity, int index, ref GhostSystemStateComponent ghost)
            {
                uint ackedByAllTick = tick[0];
                if (ghost.despawnTick == 0)
                {
                    ghost.despawnTick = currentTick;
                }
                else if (ackedByAllTick != 0 && !SequenceHelpers.IsNewer(ghost.despawnTick, ackedByAllTick))
                {
                    freeGhostIds.Enqueue(ghost.ghostId);
                    commandBuffer.RemoveComponent(index, entity, ghostStateType);
                }
            }
        }

        // Not burst compiled due to commandBuffer.SetComponent
        struct SpawnGhostJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk>           spawnChunks;
            public            NativeList<PrioChunk>                 serialSpawnChunks;
            [ReadOnly] public ArchetypeChunkEntityType              entityType;
            public            NativeArray<GhostSerializerReference> serializers;
            public            NativeQueue<int>                      freeGhostIds;
            public            NativeArray<int>                      allocatedGhostIds;
            public            EntityCommandBuffer                   commandBuffer;

            public unsafe void Execute()
            {
                for (int chunk = 0; chunk < spawnChunks.Length; ++chunk)
                {
                    var entities = spawnChunks[chunk].GetNativeArray(entityType);
                    var ghostState = (GhostSystemStateComponent*) UnsafeUtility.Malloc(
                        UnsafeUtility.SizeOf<GhostSystemStateComponent>() * entities.Length,
                        UnsafeUtility.AlignOf<GhostSystemStateComponent>(), Allocator.TempJob);

                    for (var i = 0; i != serializers.Length; i++)
                    {
                        ref var serializerBase   = ref serializers[i].AsRef();
                        ref var serializerHeader = ref serializerBase.Header;

                        if (!serializerHeader.CanSerializeFunc.Invoke(ref serializerBase, spawnChunks[chunk].Archetype))
                            continue;

                        var pc = new PrioChunk
                        {
                            chunk      = spawnChunks[chunk],
                            ghostState = ghostState,
                            priority   = serializerHeader.Importance,
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

                        ghostState[ent] = new GhostSystemStateComponent {ghostId = newId, despawnTick = 0};

                        // This runs after simulation. If an entity is created in the begin barrier and destroyed before this
                        // runs there can be errors. To get around those we add the ghost system state component before everything
                        // using the begin barrier, and set the value here
                        commandBuffer.SetComponent(entities[ent], ghostState[ent]);
                    }
                }
            }
        }

        [BurstCompile]
        struct SerializeJob : IJob
        {
            [ReadOnly] public NativeArray<ArchetypeChunk> despawnChunks;
            [ReadOnly] public NativeArray<ArchetypeChunk> ghostChunks;

            public            Entity                                            connectionEntity;
            public            NativeHashMap<ArchetypeChunk, SerializationStateList> chunkSerializationData;
            [ReadOnly] public ComponentDataFromEntity<NetworkSnapshotAck>       ackFromEntity;

            [NativeDisableContainerSafetyRestriction]
            public BufferFromEntity<OutgoingSnapshotDataStreamBufferComponent> bufferFromEntity;

            [ReadOnly] public NativeList<PrioChunk> serialSpawnChunks;

            [ReadOnly] public ArchetypeChunkEntityType                               entityType;
            [ReadOnly] public ArchetypeChunkComponentType<GhostSystemStateComponent> ghostSystemStateType;

            [ReadOnly] public NativeArray<GhostSerializerReference> serializers;
            [ReadOnly] public NetworkCompressionModel   compressionModel;


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
                var  serialChunks = new NativeList<PrioChunk>(ghostChunks.Length + serialSpawnChunks.Length, Allocator.Temp);
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
                        chunkStateList = new SerializationStateList(Allocator.Persistent, ghostChunks[chunk].Count);
                        chunkStateList.Archetype = ghostChunks[chunk].Archetype;
                        
                        for (var i = 0; i != serializers.Length; i++)
                        {
                            ref var serializerBase = ref serializers[i].AsRef();
                            if (!serializerBase.Header.CanSerializeFunc.Invoke(ref serializerBase, ghostChunks[chunk].Archetype))
                                continue;

                            var serializerDataSize = serializerBase.Header.SnapshotSize;

                            chunkStateList.Add(new SerializationState
                            {
                                lastUpdate = currentTick - 1,
                                startIndex = 0,
                                ghostType  = i,
                                arch = ghostChunks[chunk].Archetype,
                                
                                snapshotWriteIndex = 0,
                                snapshotData = (byte*) UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>() * SnapshotHistorySize + SnapshotHistorySize * ghostChunks[chunk].Capacity * (entitySize + serializerDataSize), 16, Allocator.Persistent)
                            });
                            
                            UnsafeUtility.MemClear(chunkStateList[chunkStateList.Length - 1].snapshotData, UnsafeUtility.SizeOf<int>() * SnapshotHistorySize);
                        }

                        chunkSerializationData.TryAdd(ghostChunks[chunk], chunkStateList);
                    }

                    existingChunks.TryAdd(ghostChunks[chunk], 1);
                    // FIXME: only if modified or force sync
                    for (var i = 0; i != chunkStateList.Length; i++)
                    {
                        var pc = new PrioChunk();
                        pc.chunk = ghostChunks[chunk];
                        pc.ghostState = null;
                        pc.priority = serializers[chunkStateList[i].ghostType].Value->Header.Importance * (int) (currentTick - chunkStateList[i].lastUpdate);
                        pc.startIndex = chunkStateList[i].startIndex;
                        pc.ghostType = chunkStateList[i].ghostType;

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

                NativeArray<PrioChunk> serialChunkArray = serialChunks;
                serialChunkArray.Sort();
                var availableBaselines = new NativeList<SnapshotBaseline>(SnapshotHistorySize, Allocator.Temp);
                var baselinePerEntity  = new NativeArray<int>(maxCount * 3, Allocator.Temp);
                for (int pc = 0; pc < serialChunks.Length && dataStream.Length < TargetPacketSize; ++pc) // serialChunks can have the same chunks present
                {
                    var chunk     = serialChunks[pc].chunk;
                    var ghostType = serialChunks[pc].ghostType;

                    Entity*            currentSnapshotEntity = null;
                    byte*              currentSnapshotData   = null;
                    availableBaselines.Clear();
                    if (chunkSerializationData.TryGetValue(chunk, out var chunkStateList))
                    {
                        for (var i = 0; i != chunkStateList.Length; i++)
                        {
                            var chunkState = chunkStateList[i];
                            var serializer = serializers[chunkState.ghostType];
                            var dataSize = serializer.Value->Header.SnapshotSize;

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
                                    availableBaselines.Add(new SnapshotBaseline
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
                        ghosts = (GhostSystemStateComponent*) chunk.GetNativeArray(ghostSystemStateType).GetUnsafeReadOnlyPtr();
                    }

                    var ghostEntities = chunk.GetNativeArray(entityType);
                    int ent;
                    if (serialChunks[pc].startIndex < chunk.Count)
                    {
                        dataStream.WritePackedUInt((uint) ghostType, compressionModel);
                        dataStream.WritePackedUInt((uint) (chunk.Count - serialChunks[pc].startIndex), compressionModel);
                    }

                    // First figure out the baselines to use per entity so they can be sent as baseline + maxCount instead of one per entity
                    int targetBaselines = serializers[ghostType].Value->Header.WantsPredictionDelta ? 3 : 1;
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

                    ent = GhostSendSystem.InvokeSerialize(ref serializers[ghostType].AsRef(), ghostType, chunk, serialChunks[pc].startIndex, currentTick,
                        currentSnapshotEntity, currentSnapshotData, ghosts, ghostEntities,
                        baselinePerEntity, availableBaselines, dataStream, compressionModel);
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

                var snapshot = bufferFromEntity[connectionEntity];
                snapshot.ResizeUninitialized(dataStream.Length);
                UnsafeUtility.MemCpy(snapshot.GetUnsafePtr(), dataStream.GetUnsafePtr(), dataStream.Length);

            }
        }

        public unsafe struct SnapshotBaseline
        {
            public uint    tick;
            public byte*   snapshot;
            public Entity* entity;
        }

        [BurstCompile]
        struct CleanupJob : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           despawnChunks;
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           spawnChunks;
            [DeallocateOnJobCompletion] public NativeArray<ArchetypeChunk>           ghostChunks;
            [DeallocateOnJobCompletion] public NativeArray<GhostSerializerReference> serializers;
            public                             NativeList<PrioChunk>                 serialSpawnChunks;

            public unsafe void Execute()
            {
                for (int i = 0; i != serializers.Length; i++)
                {
                    serializers[i].Dispose();
                }
                
                for (int i = 0; i < serialSpawnChunks.Length; ++i)
                {
                    UnsafeUtility.Free(serialSpawnChunks[i].ghostState, Allocator.TempJob);
                }
            }
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            m_SerialSpawnChunks.Clear();
            // Make sure the list of connections and connection state is up to date
            var connections = connectionGroup.ToEntityArray(Allocator.TempJob);
            var existing    = new NativeHashMap<Entity, int>(connections.Length, Allocator.Temp);
            for (int i = 0; i < connections.Length; ++i)
            {
                existing.TryAdd(connections[i], 1);
                int stateIndex;
                if (!m_ConnectionStateLookup.TryGetValue(connections[i], out stateIndex))
                {
                    m_ConnectionStates.Add(new ConnectionStateData
                    {
                        Entity             = connections[i],
                        SerializationState = new NativeHashMap<ArchetypeChunk, SerializationStateList>(1024, Allocator.Persistent)
                    });
                    m_ConnectionStateLookup.TryAdd(connections[i], m_ConnectionStates.Count - 1);
                }
            }

            connections.Dispose();

            for (int i = 0; i < m_ConnectionStates.Count; ++i)
            {
                int val;
                if (!existing.TryGetValue(m_ConnectionStates[i].Entity, out val))
                {
                    m_ConnectionStateLookup.Remove(m_ConnectionStates[i].Entity);
                    m_ConnectionStates[i].Dispose();
                    if (i != m_ConnectionStates.Count - 1)
                    {
                        m_ConnectionStates[i] = m_ConnectionStates[m_ConnectionStates.Count - 1];
                        m_ConnectionStateLookup.Remove(m_ConnectionStates[i].Entity);
                        m_ConnectionStateLookup.TryAdd(m_ConnectionStates[i].Entity, i);
                    }

                    m_ConnectionStates.RemoveAt(m_ConnectionStates.Count - 1);
                }
            }

            // Find the latest tick which has been acknowledged by all clients and cleanup all ghosts destroyed ebfore that
            uint currentTick = m_ServerSimulation.ServerTick;

            var ackedByAll = new NativeArray<uint>(1, Allocator.TempJob);
            ackedByAll[0] = currentTick;
            var findAckJob = new FindAckedByAllJob
            {
                tick = ackedByAll
            };
            inputDeps = findAckJob.ScheduleSingle(this, inputDeps);

            EntityCommandBuffer commandBuffer = m_Barrier.CreateCommandBuffer();
            var ghostCleanupJob = new CleanupGhostJob
            {
                currentTick    = currentTick,
                tick           = ackedByAll,
                commandBuffer  = commandBuffer.ToConcurrent(),
                freeGhostIds   = m_FreeGhostIds.ToConcurrent(),
                ghostStateType = ComponentType.ReadWrite<GhostSystemStateComponent>()
            };
            inputDeps = ghostCleanupJob.Schedule(this, inputDeps);


            var entityType           = GetArchetypeChunkEntityType();
            var ghostSystemStateType = GetArchetypeChunkComponentType<GhostSystemStateComponent>();
            var serializerSystem = World.GetExistingSystem<GhostSerializerCollectionSystem>();
            var serializers = serializerSystem.BeginSerialize(this, Allocator.TempJob);

            // Extract all newly spawned ghosts and set their ghost ids
            JobHandle spawnChunkHandle;
            var       spawnChunks = ghostSpawnGroup.CreateArchetypeChunkArray(Allocator.TempJob, out spawnChunkHandle);
            var spawnJob = new SpawnGhostJob
            {
                spawnChunks       = spawnChunks,
                serialSpawnChunks = m_SerialSpawnChunks,
                entityType        = entityType,
                serializers       = serializers,
                freeGhostIds      = m_FreeGhostIds,
                allocatedGhostIds = m_AllocatedGhostIds,
                commandBuffer     = commandBuffer
            };
            inputDeps = spawnJob.Schedule(JobHandle.CombineDependencies(inputDeps, spawnChunkHandle));
            // This was the last job using the commandBuffer
            m_Barrier.AddJobHandleForProducer(inputDeps);

            JobHandle despawnChunksHandle, ghostChunksHandle;
            var       despawnChunks = ghostDespawnGroup.CreateArchetypeChunkArray(Allocator.TempJob, out despawnChunksHandle);
            var       ghostChunks   = ghostGroup.CreateArchetypeChunkArray(Allocator.TempJob, out ghostChunksHandle);
            inputDeps = JobHandle.CombineDependencies(inputDeps, despawnChunksHandle, ghostChunksHandle);

            var serialDep = new NativeArray<JobHandle>(m_ConnectionStates.Count + 1, Allocator.Temp);
            // In case there are 0 connections
            serialDep[0] = inputDeps;
            for (int con = 0; con < m_ConnectionStates.Count; ++con)
            {
                var connectionEntity       = m_ConnectionStates[con].Entity;
                var chunkSerializationData = m_ConnectionStates[con].SerializationState;
                var serializeJob = new SerializeJob
                {
                    despawnChunks          = despawnChunks,
                    ghostChunks            = ghostChunks,
                    connectionEntity       = connectionEntity,
                    chunkSerializationData = chunkSerializationData,
                    ackFromEntity          = GetComponentDataFromEntity<NetworkSnapshotAck>(true),
                    bufferFromEntity       = GetBufferFromEntity<OutgoingSnapshotDataStreamBufferComponent>(),
                    serialSpawnChunks      = m_SerialSpawnChunks,
                    entityType             = entityType,
                    ghostSystemStateType   = ghostSystemStateType,
                    serializers            = serializers,
                    compressionModel       = m_CompressionModel,
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
                serialSpawnChunks = m_SerialSpawnChunks,
                serializers       = serializers
            };
            inputDeps = cleanupJob.Schedule(inputDeps);

            return inputDeps;
        }

        unsafe struct PrioChunk : IComparable<PrioChunk>
        {
            public ArchetypeChunk             chunk;
            public GhostSystemStateComponent* ghostState;
            public int                        priority;
            public int                        startIndex;
            public int                        ghostType;

            public int CompareTo(PrioChunk other)
            {
                // Reverse priority for sorting
                return other.ghostType - ghostType + other.priority - priority;
            }
        }

        public static unsafe int  InvokeSerialize(ref GhostSerializerBase serializerBaseData,            int                          ghostType, ArchetypeChunk chunk, int startIndex, uint currentTick,
                                                                             Entity*                    currentSnapshotEntity, byte*               currentSnapshotData,
                                                                             GhostSystemStateComponent* ghosts,                NativeArray<Entity>          ghostEntities,
                                                                             NativeArray<int>           baselinePerEntity,     NativeList<SnapshotBaseline> availableBaselines,
                                                                             DataStreamWriter           dataStream,            NetworkCompressionModel      compressionModel)
        {
            int ent;
            int sameBaselineCount = 0;

            var start = dataStream.Length;
            for (ent = startIndex; ent < chunk.Count && dataStream.Length < TargetPacketSize; ++ent)
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                /*if (ghosts[ent].ghostTypeIndex != ghostType)
                {
                    // FIXME: what needs to happen to support this case? Should it be treated as a respawn?
                    throw new InvalidOperationException("A ghost changed type, ghost must keep the same serializer type throughout their lifetime");
                }*/
#endif

                int baseline0 = baselinePerEntity[ent * 3];
                int baseline1 = baselinePerEntity[ent * 3 + 1];
                int baseline2 = baselinePerEntity[ent * 3 + 2];
                if (sameBaselineCount == 0)
                {
                    // Count how many entities will use the same baselines as this one, send baselines + count
                    uint baselineTick0 = currentTick;
                    uint baselineTick1 = currentTick;
                    uint baselineTick2 = currentTick;
                    if (baseline0 >= 0)
                    {
                        baselineTick0 = availableBaselines[baseline0].tick;
                    }

                    if (baseline1 >= 0)
                    {
                        baselineTick1 = availableBaselines[baseline1].tick;
                    }

                    if (baseline2 >= 0)
                    {
                        baselineTick2 = availableBaselines[baseline2].tick;
                    }

                    for (sameBaselineCount = 1; ent + sameBaselineCount < chunk.Count; ++sameBaselineCount)
                    {
                        if (baselinePerEntity[(ent + sameBaselineCount) * 3] != baseline0 ||
                            baselinePerEntity[(ent + sameBaselineCount) * 3 + 1] != baseline1 ||
                            baselinePerEntity[(ent + sameBaselineCount) * 3 + 2] != baseline2)
                            break;
                    }

                    uint baseDiff0 = currentTick - baselineTick0;
                    uint baseDiff1 = currentTick - baselineTick1;
                    uint baseDiff2 = currentTick - baselineTick2;
                    dataStream.WritePackedUInt(baseDiff0, compressionModel);
                    dataStream.WritePackedUInt(baseDiff1, compressionModel);
                    dataStream.WritePackedUInt(baseDiff2, compressionModel);
                    dataStream.WritePackedUInt((uint) sameBaselineCount, compressionModel);
                }

                --sameBaselineCount;
                byte* baselineSnapshotData0 = null;
                if (baseline0 >= 0)
                {
                    baselineSnapshotData0 = (availableBaselines[baseline0].snapshot) + ent;
                }

                byte* baselineSnapshotData1 = null;
                byte* baselineSnapshotData2 = null;
                if (baseline2 >= 0)
                {
                    baselineSnapshotData1 = (availableBaselines[baseline1].snapshot) + ent;
                    baselineSnapshotData2 = (availableBaselines[baseline2].snapshot) + ent;
                }


                dataStream.WritePackedUInt((uint) ghosts[ent].ghostId, compressionModel);

                byte* snapshot;
                var            snapshotData = (byte*) UnsafeUtility.Malloc(serializerBaseData.Header.SnapshotSize, serializerBaseData.Header.SnapshotAlign, Allocator.TempJob);
                if (currentSnapshotData == null)
                    snapshot = snapshotData;
                else
                    snapshot = currentSnapshotData + ent;

                serializerBaseData.Header.CopyToSnapshotFunc.Invoke(ref serializerBaseData, chunk, ent, currentTick, snapshot);
                
                var            baselineData = (byte*) UnsafeUtility.Malloc(serializerBaseData.Header.SnapshotSize, serializerBaseData.Header.SnapshotAlign, Allocator.TempJob);
                UnsafeUtility.MemClear(baselineData, serializerBaseData.Header.SnapshotSize);
                
                byte* baseline     = baselineData;
                if (baselineSnapshotData2 != null)
                {
                    baselineData = baselineSnapshotData0;

                    serializerBaseData.Header.PredictDeltaFunc.Invoke(ref serializerBaseData, currentTick, baselineData, baselineSnapshotData1, baselineSnapshotData2);
                }
                else if (baselineSnapshotData0 != null)
                {
                    UnsafeUtility.MemCpy(baseline, baselineSnapshotData0, serializerBaseData.Header.SnapshotSize);
                }

                serializerBaseData.Header.SerializeEntityFunc.Invoke(ref serializerBaseData, snapshot, baseline, UnsafeUtility.AddressOf(ref dataStream), UnsafeUtility.AddressOf(ref compressionModel));
                
                if (currentSnapshotData != null)
                    currentSnapshotEntity[ent] = ghostEntities[ent];
                
                UnsafeUtility.Free(snapshotData, Allocator.TempJob);
                UnsafeUtility.Free(baselineData, Allocator.TempJob);
            }
            
            return ent;
        }
    }

    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateBefore(typeof(NetworkStreamReceiveSystem))]
    public class AddNetworkIdSystem : JobComponentSystem
    {
        [ExcludeComponent(typeof(GhostSendSystem.GhostSystemStateComponent))]
        struct AddJob : IJobProcessComponentDataWithEntity<GhostComponent>
        {
            public EntityCommandBuffer.Concurrent commandBuffer;

            public void Execute(Entity entity, int entityIndex, [ReadOnly] ref GhostComponent ghost)
            {
                commandBuffer.AddComponent(entityIndex, entity, new GhostSendSystem.GhostSystemStateComponent());
            }
        }

        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var job = new AddJob();
            job.commandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            inputDeps         = job.Schedule(this, inputDeps);
            m_Barrier.AddJobHandleForProducer(inputDeps);
            return inputDeps;
        }
    }
}
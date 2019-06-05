using System;
using System.Collections.Generic;
using StormiumTeam.Networking;
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
    [AlwaysUpdateSystem]
    public class GhostSendSystem : JobComponentSystem
    {
        private DataStreamWriter m_DataStream;

        public struct GhostSystemStateComponent : ISystemStateComponentData
        {
            public int  ghostId;
            public uint despawnTick;
        }

        public unsafe struct InvokeData
        {
            public int serializer;
            public ArchetypeChunk chunk;
            public int startIndex;
            public uint currentTick;

            public Entity* currentSnapshotEntity;
            public void* currentSnapshotData;

            public GhostSendSystem.GhostSystemStateComponent* ghosts;
            public NativeArray<Entity> ghostEntities;

            public NativeArray<int> baselinePerEntity;
            public NativeList<GhostSendSystem.SnapshotBaseline> availableBaselines;

            public DataStreamWriter dataStream;
            public NetworkCompressionModel compressionModel;
        }

        public unsafe struct SerializationStateList : IDisposable
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

        public unsafe struct SerializationState
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

        public unsafe struct ConnectionStateData : IDisposable
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

        public const int TargetPacketSize    = 1200;
        public const  int SnapshotHistorySize = 32;

        private ServerSimulationSystemGroup              m_ServerSimulation;
        private BeginSimulationEntityCommandBufferSystem m_Barrier;

        protected override void OnCreate()
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
                ComponentType.ReadOnly<NetworkStreamInGame>());

            m_ServerSimulation = World.GetExistingSystem<ServerSimulationSystemGroup>();
            m_Barrier          = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();

            m_ConnectionStates      = new List<ConnectionStateData>(256);
            m_ConnectionStateLookup = new NativeHashMap<Entity, int>(256, Allocator.Persistent);
            m_CompressionModel      = new NetworkCompressionModel(Allocator.Persistent);

            m_SerialSpawnChunks = new NativeList<PrioChunk>(1024, Allocator.Persistent);
        }

        protected override void OnDestroy()
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
        struct FindAckedByAllJob : IJobProcessComponentData<NetworkSnapshotAckComponent>
        {
            public NativeArray<uint> tick;

            public void Execute([ReadOnly] ref NetworkSnapshotAckComponent ack)
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

        public unsafe struct SnapshotBaseline
        {
            public uint    tick;
            public byte*   snapshot;
            public Entity* entity;
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var serializerCollectionSystem = World.GetExistingSystem<GhostSerializerCollectionSystem>();
            if (serializerCollectionSystem.CollectionSystem == null)
            {
                serializerCollectionSystem.CollectionSystem = new DynamicGhostCollectionSystem();
            }

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

            inputDeps = serializerCollectionSystem.CollectionSystem.ExecuteSend(this, m_ConnectionStates,
                ghostSpawnGroup, ghostDespawnGroup, ghostGroup,
                m_SerialSpawnChunks,
                m_FreeGhostIds, m_AllocatedGhostIds,
                commandBuffer,
                m_Barrier,
                m_CompressionModel,
                currentTick,
                inputDeps);

            return inputDeps;
        }

        public unsafe struct PrioChunk : IComparable<PrioChunk>
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

        public static unsafe int InvokeSerialize<TSerializer, TSnapshotData>(TSerializer serializer, InvokeData invokeData)
            where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
            where TSerializer : struct, IGhostSerializer<TSnapshotData>
        {
            return InvokeSerialize(serializer, invokeData.chunk, invokeData.startIndex, invokeData.currentTick,
                invokeData.currentSnapshotEntity, (TSnapshotData*) invokeData.currentSnapshotData,
                invokeData.ghosts, invokeData.ghostEntities,
                invokeData.baselinePerEntity, invokeData.availableBaselines,
                invokeData.dataStream, invokeData.compressionModel);
        }

        public static unsafe int InvokeSerialize<TSerializer, TSnapshotData>(TSerializer serializer, ArchetypeChunk chunk, int startIndex, uint currentTick,
                                                 Entity*                    currentSnapshotEntity, TSnapshotData*                        currentSnapshotData,
                                                 GhostSystemStateComponent* ghosts,                NativeArray<Entity>          ghostEntities,
                                                 NativeArray<int>           baselinePerEntity,     NativeList<SnapshotBaseline> availableBaselines,
                                                 DataStreamWriter           dataStream,            NetworkCompressionModel      compressionModel)
            where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
            where TSerializer : struct, IGhostSerializer<TSnapshotData>
        {
            int ent;
            int sameBaselineCount = 0;

            for (ent = startIndex; ent < chunk.Count && dataStream.Length < TargetPacketSize; ++ent)
            {
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
                TSnapshotData* baselineSnapshotData0 = null;
                if (baseline0 >= 0)
                {
                    baselineSnapshotData0 = ((TSnapshotData*) availableBaselines[baseline0].snapshot) + ent;
                }

                TSnapshotData* baselineSnapshotData1 = null;
                TSnapshotData* baselineSnapshotData2 = null;
                if (baseline2 >= 0)
                {
                    baselineSnapshotData1 = ((TSnapshotData*) availableBaselines[baseline1].snapshot) + ent;
                    baselineSnapshotData2 = ((TSnapshotData*) availableBaselines[baseline2].snapshot) + ent;
                }

                dataStream.WritePackedUInt((uint) ghosts[ent].ghostId, compressionModel);

                TSnapshotData* snapshot;
                var   snapshotData = default(TSnapshotData);
                if (currentSnapshotData == null)
                    snapshot = &snapshotData;
                else
                    snapshot = currentSnapshotData + ent;

                serializer.CopyToSnapshot(chunk, ent, currentTick, ref *snapshot);

                var baselineData = default(TSnapshotData);

                TSnapshotData* baseline = &baselineData;
                if (baselineSnapshotData2 != null)
                {
                    baselineData = *baselineSnapshotData0;
                    baselineData.PredictDelta(currentTick, ref *baselineSnapshotData1, ref *baselineSnapshotData2);
                }
                else if (baselineSnapshotData0 != null)
                {
                    baseline = baselineSnapshotData0;
                }

               snapshot->Serialize(ref *baseline, dataStream, compressionModel);

                if (currentSnapshotData != null)
                    currentSnapshotEntity[ent] = ghostEntities[ent];
            }

            return ent;
        }

        public static unsafe int  InvokeSerialize(ref GhostSerializerBase serializerBaseData,            int                          ghostType, ArchetypeChunk chunk, int startIndex, uint currentTick,
                                                                             Entity*                    currentSnapshotEntity, byte*               currentSnapshotData,
                                                                             GhostSystemStateComponent* ghosts,                NativeArray<Entity>          ghostEntities,
                                                                             NativeArray<int>           baselinePerEntity,     NativeList<SnapshotBaseline> availableBaselines,
                                                                             DataStreamWriter           dataStream,            NetworkCompressionModel      compressionModel)
        {
            int ent;
            int sameBaselineCount = 0;

            for (ent = startIndex; ent < chunk.Count && dataStream.Length < TargetPacketSize; ++ent)
            {
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

        protected override void OnCreate()
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
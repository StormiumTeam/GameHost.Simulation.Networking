using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.NetCode
{
    public interface IGhostCollection
    {
        int Length { get; }

        void Initialize(World world);
        
        void BeginSerialize(ComponentSystemBase system);
        void BeginDeserialize(JobComponentSystem system);
        
        int  CalculateImportance(int              serializer, ArchetypeChunk chunk);
        bool WantsPredictionDelta(int serializer);
        int  GetSnapshotSize(int                  serializer);
        
        bool CanSerialize(int                     serializer, EntityArchetype archetype);
        int  Serialize(GhostSendSystem.InvokeData invokeData);
        void Deserialize(GhostReceiveSystem.InvokeDeserializeData invokeDeserializeData);
        void Spawn(GhostReceiveSystem.InvokeSpawnData invokeSpawnData);
    }

    public abstract class BaseGhostCollectionSystem
    {
        public const int SnapshotHistorySize = GhostSendSystem.SnapshotHistorySize;
        public const int TargetPacketSize    = GhostSendSystem.TargetPacketSize;

        public abstract JobHandle ExecuteSend(GhostSendSystem                           sendSystem,
                                          List<GhostSendSystem.ConnectionStateData> connectionStates,
                                          EntityQuery                               ghostSpawnGroup, EntityQuery ghostDespawnGroup, EntityQuery ghostGroup,
                                          NativeList<GhostSendSystem.PrioChunk>     serialSpawnChunks,
                                          NativeQueue<int>                          freeGhostIds,  NativeArray<int>                         allocatedGhostIds,
                                          EntityCommandBuffer                       commandBuffer, BeginSimulationEntityCommandBufferSystem barrier,
                                          NetworkCompressionModel                   compressionModel,
                                          uint                                      currentTick,
                                          JobHandle                                 inputDeps);

        public abstract JobHandle ExecuteReceive(GhostReceiveSystem receiveSystem, 
                                                 EntityQuery playerGroup,
                                                 NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostEntityMap, NativeQueue<GhostReceiveSystem.DelayedDespawnGhost> delayedDespawnQueue, 
                                                 NetworkCompressionModel compressionModel, 
                                                 EntityCommandBuffer commandBuffer, BeginSimulationEntityCommandBufferSystem barrier, 
                                                 ref JobHandle dependency, JobHandle inputDeps);
    }
}
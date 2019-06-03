using System;
using StormiumTeam.Networking;
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

        public unsafe struct InvokeSpawnData
        {
            public int ghostId;
            
            public int serializer;
            public uint snapshot;
            public DataStreamReader          reader;
            public DataStreamReader.Context* context;
            public NetworkCompressionModel   compressionModel;
        }
        
        public unsafe struct InvokeDeserializeData
        {
            public int                       serializer;
            public Entity                    entity;
            public uint                      snapshot;
            public uint                      baseline;
            public uint                      baseline2;
            public uint                      baseline3;
            public DataStreamReader          reader;
            public DataStreamReader.Context* context;
            public NetworkCompressionModel   compressionModel;
        }

        public struct GhostEntity
        {
            public Entity entity;
        }

        public struct DelayedDespawnGhost
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

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var serializerCollectionSystem = World.GetExistingSystem<GhostSerializerCollectionSystem>();
            if (serializerCollectionSystem.CollectionSystem == null)
            {
                serializerCollectionSystem.CollectionSystem = new DynamicGhostCollectionSystem();
            }

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

            return serializerCollectionSystem.CollectionSystem.ExecuteReceive(this,
                playerGroup,
                m_ghostEntityMap, m_DelayedDespawnQueue,
                m_CompressionModel,
                commandBuffer, m_Barrier,
                ref m_Dependency, inputDeps);
        }

        public static unsafe T InvokeSpawn<T>(InvokeSpawnData invokeData)
            where T : struct, ISnapshotData<T>
        {
            return InvokeSpawn<T>(invokeData.snapshot,
                invokeData.reader, ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(invokeData.context), invokeData.compressionModel);
        }

        public static T InvokeSpawn<T>(uint             snapshot,
                                       DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
            where T : struct, ISnapshotData<T>
        {
            var snapshotData = default(T);
            var baselineData = default(T);
            snapshotData.Deserialize(snapshot, ref baselineData, reader, ref ctx, compressionModel);
            return snapshotData;
        }


        public static unsafe void InvokeDeserialize<T>(BufferFromEntity<T> snapshotFromEntity, InvokeDeserializeData invokeData)
            where T : struct, ISnapshotData<T>
        {
            InvokeDeserialize(snapshotFromEntity,
                invokeData.entity, invokeData.snapshot, invokeData.baseline, invokeData.baseline2, invokeData.baseline3,
                invokeData.reader, ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(invokeData.context), invokeData.compressionModel);
        }

        public static void InvokeDeserialize<T>(BufferFromEntity<T> snapshotFromEntity,
                                                Entity              entity, uint                         snapshot, uint                    baseline, uint baseline2, uint baseline3,
                                                DataStreamReader    reader, ref DataStreamReader.Context ctx,      NetworkCompressionModel compressionModel)
            where T: struct, ISnapshotData<T>
        {
            DynamicBuffer<T> snapshotArray = snapshotFromEntity[entity];
            var              baselineData  = default(T);
            if (baseline != snapshot)
            {
                for (int i = 0; i < snapshotArray.Length; ++i)
                {
                    if (snapshotArray[i].Tick == baseline)
                    {
                        baselineData = snapshotArray[i];
                        break;
                    }
                }
            }
            if (baseline3 != snapshot)
            {
                var baselineData2 = default(T);
                var baselineData3 = default(T);
                for (int i = 0; i < snapshotArray.Length; ++i)
                {
                    if (snapshotArray[i].Tick == baseline2)
                    {
                        baselineData2 = snapshotArray[i];
                    }
                    if (snapshotArray[i].Tick == baseline3)
                    {
                        baselineData3 = snapshotArray[i];
                    }
                }

                baselineData.PredictDelta(snapshot, ref baselineData2, ref baselineData3);
            }
            var data = default(T);
            data.Deserialize(snapshot, ref baselineData, reader, ref ctx, compressionModel);
            // Replace the oldest snapshot, or add a new one
            if (snapshotArray.Length == GhostSendSystem.SnapshotHistorySize)
                snapshotArray.RemoveAt(0);
            snapshotArray.Add(data);
        }
    }
}
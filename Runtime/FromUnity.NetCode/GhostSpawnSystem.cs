using System;
using Scripts.Utilities;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.NetCode
{
    internal struct DelayedSpawnGhost
    {
        public int    ghostId;
        public uint   spawnTick;
        public Entity oldEntity;
    }
    
    internal struct NewGhost : IEquatable<NewGhost>
    {
        public int  id;
        public uint tick;

        public bool Equals(NewGhost other)
        {
            return id == other.id;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is NewGhost other && Equals(other);
        }

        public override int GetHashCode()
        {
            return id;
        }
    }

    public abstract class BaseGhostManageSerializerSystem : JobComponentSystem
    {
        internal abstract JobHandle InternalUpdateNewEntities(NativeArray<Entity> entities, NativeList<DelayedSpawnGhost> delayedGhost, NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle inputDeps);
        internal abstract JobHandle InternalFinalPass(NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle jobHandle);

        public abstract ComponentTypes GetComponents();

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            return inputDeps;
        }
    }

    [UpdateInGroup(typeof(GhostManageSerializerGroup))]
    public abstract class BaseGhostManageSerializer<T, TSerializer> : BaseGhostManageSerializerSystem
        where TSerializer : struct, IGhostSerializer<T>
        where T : unmanaged, ISnapshotData<T>
    {
        private int m_SerializerId;
        
        public ResizableList<T> NewGhosts;
        public ResizableList<int> NewGhostIds;
        
        [BurstCompile]
        private struct DelayedJob : IJob
        {
            [ReadOnly]                            public NativeArray<Entity>                                entities;
            [ReadOnly]                            public NativeList<DelayedSpawnGhost>                      delayedGhost;
            [NativeDisableParallelForRestriction] public BufferFromEntity<T>                                snapshotFromEntity;

            public void Execute()
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    var newSnapshot = snapshotFromEntity[entities[i]];
                    var oldSnapshot = snapshotFromEntity[delayedGhost[i].oldEntity];
                    newSnapshot.ResizeUninitialized(oldSnapshot.Length);
                    for (int snap = 0; snap < newSnapshot.Length; ++snap)
                        newSnapshot[snap] = oldSnapshot[snap];
                }
            }
        }

        [BurstCompile]
        private struct CopyInitialStateJob : IJobParallelFor
        {
            public int SerializerId;
            
            [ReadOnly] public NativeArray<GhostReceiveSystem.GhostEntity>                                           entities;
            [ReadOnly]                            public ResizableList<T>                                                 newGhosts;
            [NativeDisableParallelForRestriction] public BufferFromEntity<T>                                           snapshotFromEntity;
            [NativeDisableParallelForRestriction] public BufferFromEntity<ReplicatedEntitySerializer> serializerFromEntity;

            public void Execute(int i)
            {
                var snapshot = snapshotFromEntity[entities[i].entity];
                snapshot.ResizeUninitialized(1);
                snapshot[0] = newGhosts[i];

                serializerFromEntity[entities[i].entity].AddSerializer(SerializerId);
            }
        }

        private struct UpdateBufferJob : IJobParallelFor
        {
            public NativeArray<Entity> entities;
            [NativeDisableUnsafePtrRestriction] public BufferFromEntity<T> snapshotFromEntity;
            
            public void Execute(int index)
            {
                
            }
        }

        [BurstCompile]
        private struct ClearJob : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<GhostReceiveSystem.GhostEntity> entities;

            public ResizableList<T> newGhosts;
            public ResizableList<int> newGhostIds;
            
            public void Execute()
            {
                newGhosts.Clear();
                newGhostIds.Clear();
            }
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            NewGhosts   = new ResizableList<T>(Allocator.Persistent, 16);
            NewGhostIds = new ResizableList<int>(Allocator.Persistent, 16);
            
            World.GetOrCreateSystem<GhostManageSerializerGroup>().AddSystemToUpdateList(this);

            if (!World.GetOrCreateSystem<GhostSerializerCollectionSystem>().TryAdd<TSerializer, T>(out var serializer))
            {
            }

            m_SerializerId = serializer.Header.Id;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            
            NewGhosts.Dispose();
            NewGhostIds.Dispose();
        }

        internal override JobHandle InternalFinalPass(NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle jobHandle)
        {
            if (NewGhosts.Length <= 0)
                return jobHandle;

            Profiler.BeginSample("InternalFinalPass()");
            jobHandle.Complete();

            var entities = new NativeArray<GhostReceiveSystem.GhostEntity>(NewGhosts.Length, Allocator.TempJob);
            for (var i = 0; i != entities.Length; i++)
            {
                entities[i] = ghostMap[NewGhostIds[i]];

                var components = GetComponents();
                for (var j = 0; j != components.Length; j++)
                {
                    if (!EntityManager.HasComponent(entities[i].entity, components.GetComponentType(j)))
                    {
                        EntityManager.AddComponent(entities[i].entity, components.GetComponentType(j));
                    }
                }
            }

            jobHandle = new CopyInitialStateJob
            {
                SerializerId         = m_SerializerId,
                entities             = entities,
                newGhosts            = NewGhosts,
                snapshotFromEntity   = GetBufferFromEntity<T>(),
                serializerFromEntity = GetBufferFromEntity<ReplicatedEntitySerializer>()
            }.Schedule(NewGhosts.Length, 8, jobHandle);
            jobHandle = new ClearJob
            {
                entities    = entities,
                newGhosts   = NewGhosts,
                newGhostIds = NewGhostIds
            }.Schedule(jobHandle);
            Profiler.EndSample();

            return jobHandle;
        }

        protected virtual JobHandle UpdateNewEntities(NativeArray<Entity> entities, JobHandle inputDeps)
        {
            return inputDeps;
        }

        internal override JobHandle InternalUpdateNewEntities(NativeArray<Entity> entities, NativeList<DelayedSpawnGhost> delayedGhost, NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle inputDeps)
        {
            return inputDeps;
            
            for (var i = 0; i != entities.Length; i++)
            {
               // if (NewGhosts.)
            }
            
            inputDeps = new DelayedJob
            {
                entities = entities,
                delayedGhost = delayedGhost,
                snapshotFromEntity = GetBufferFromEntity<T>()
            }.Schedule(inputDeps);
            inputDeps = UpdateNewEntities(entities, inputDeps);

            return inputDeps;
        }
    }

    [UpdateInGroup(typeof(GhostSpawnSystemGroup))]
    [UpdateAfter(typeof(GhostSpawnSystem))]
    [AlwaysUpdateSystem]
    public class GhostManageSerializerGroup : ComponentSystemGroup
    {
        protected override void OnUpdate()
        {
        }

        internal JobHandle FinalPass(NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle jobHandle)
        {
            foreach (var system in Systems)
            {
                try
                {
                    jobHandle = (system as BaseGhostManageSerializerSystem).InternalFinalPass(ghostMap, jobHandle);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }

                if (World.QuitUpdate)
                    break;
            }

            return jobHandle;
        }
        
        internal JobHandle UpdateNewEntities(NativeArray<Entity> entities, NativeList<DelayedSpawnGhost> delayedGhost, NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap, JobHandle jobHandle)
        {
            foreach (var system in Systems)
            {
                try
                {
                    jobHandle = (system as BaseGhostManageSerializerSystem).InternalUpdateNewEntities(entities, delayedGhost, ghostMap, jobHandle);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex);
                }

                if (World.QuitUpdate)
                    break;
            }

            return jobHandle;
        }
    }

    [UpdateInGroup(typeof(GhostSpawnSystemGroup))]
    [AlwaysUpdateSystem]
    public class GhostSpawnSystem : JobComponentSystem
    {
        internal  NativeList<NewGhost>                                          NewGhostIds;
        
        private NativeHashMap<int, GhostReceiveSystem.GhostEntity>            m_GhostMap;
        private NativeHashMap<int, GhostReceiveSystem.GhostEntity>.Concurrent m_ConcurrentGhostMap;

        private NativeList<Entity> m_InvalidGhosts;

        private GhostManageSerializerGroup m_GhostSerializerGroup;

        private EntityArchetype m_InitialArchetype;

        private NativeQueue<DelayedSpawnGhost>            m_DelayedSpawnQueue;
        private NativeQueue<DelayedSpawnGhost>.Concurrent m_ConcurrentDelayedSpawnQueue;
        private NativeList<DelayedSpawnGhost>             m_CurrentDelayedSpawnList;

        protected override void OnCreateManager()
        {
            m_InitialArchetype = EntityManager.CreateArchetype(typeof(ReplicatedEntity), typeof(ReplicatedEntitySerializer));

            NewGhostIds = new NativeList<NewGhost>(16, Allocator.Persistent);

            m_GhostMap           = World.GetOrCreateSystem<GhostReceiveSystem>().GhostEntityMap;
            m_ConcurrentGhostMap = m_GhostMap.ToConcurrent();

            m_InvalidGhosts               = new NativeList<Entity>(1024, Allocator.Persistent);
            m_DelayedSpawnQueue           = new NativeQueue<DelayedSpawnGhost>(Allocator.Persistent);
            m_CurrentDelayedSpawnList     = new NativeList<DelayedSpawnGhost>(1024, Allocator.Persistent);
            m_ConcurrentDelayedSpawnQueue = m_DelayedSpawnQueue.ToConcurrent();

            m_GhostSerializerGroup = World.GetOrCreateSystem<GhostManageSerializerGroup>();
        }

        protected override void OnDestroyManager()
        {
            NewGhostIds.Dispose();

            m_InvalidGhosts.Dispose();
            m_DelayedSpawnQueue.Dispose();
            m_CurrentDelayedSpawnList.Dispose();
        }

        [BurstCompile]
        struct AddToGhostMapJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<Entity>                                           entities;
            [ReadOnly] public NativeList<NewGhost>                                          newGhostIds;
            public            NativeHashMap<int, GhostReceiveSystem.GhostEntity>.Concurrent ghostMap;
            public            NativeQueue<DelayedSpawnGhost>.Concurrent                     pendingSpawnQueue;

            public void Execute(int i)
            {
                ghostMap.TryAdd(newGhostIds[i].id, new GhostReceiveSystem.GhostEntity
                {
                    entity = entities[i],
                });
                pendingSpawnQueue.Enqueue(new DelayedSpawnGhost {ghostId = newGhostIds[i].id, spawnTick = newGhostIds[i].tick, oldEntity = entities[i]});
            }
        }

        [BurstCompile]
        struct DelayedSpawnJob : IJob
        {
            [ReadOnly] public NativeArray<Entity>                                entities;
            [ReadOnly] public NativeList<DelayedSpawnGhost>                      delayedGhost;
            public            NativeHashMap<int, GhostReceiveSystem.GhostEntity> ghostMap;

            public void Execute()
            {
                for (int i = 0; i < entities.Length; ++i)
                {
                    ghostMap.Remove(delayedGhost[i].ghostId);
                    ghostMap.TryAdd(delayedGhost[i].ghostId, new GhostReceiveSystem.GhostEntity
                    {
                        entity = entities[i],
                    });
                }
            }
        }

        [BurstCompile]
        struct ClearNewJob : IJob
        {
            [DeallocateOnJobCompletion] public NativeArray<Entity>  entities;
            [DeallocateOnJobCompletion] public NativeArray<Entity>  visibleEntities;
            public                             NativeList<NewGhost> newGhostIds;

            public void Execute()
            {
                newGhostIds.Clear();
            }
        }

        public JobHandle Dependency;

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            if (m_InvalidGhosts.Length > 0)
            {
                EntityManager.DestroyEntity(m_InvalidGhosts);
                m_InvalidGhosts.Clear();
            }
            
            EntityManager.CompleteAllJobs();

            //inputDeps = JobHandle.CombineDependencies(inputDeps, World.GetExistingSystem<GhostReceiveSystem>().Dependency);

            var targetTick = NetworkTimeSystem.interpolateTargetTick;
            m_CurrentDelayedSpawnList.Clear();
            while (m_DelayedSpawnQueue.Count > 0 &&
                   !SequenceHelpers.IsNewer(m_DelayedSpawnQueue.Peek().spawnTick, targetTick))
            {
                var                            ghost = m_DelayedSpawnQueue.Dequeue();
                GhostReceiveSystem.GhostEntity gent;
                if (m_GhostMap.TryGetValue(ghost.ghostId, out gent))
                {
                    m_CurrentDelayedSpawnList.Add(ghost);
                    m_InvalidGhosts.Add(gent.entity);
                }
            }

            var delayedEntities = default(NativeArray<Entity>);
            delayedEntities = new NativeArray<Entity>(m_CurrentDelayedSpawnList.Length, Allocator.TempJob);
            if (m_CurrentDelayedSpawnList.Length > 0)
            {
                EntityManager.CreateEntity(m_InitialArchetype, delayedEntities);
                for (var i = 0; i != NewGhostIds.Length; i++)
                {
                    EntityManager.GetBuffer<ReplicatedEntitySerializer>(delayedEntities[i]).Reserve(8);
                }
            }

            var entities = default(NativeArray<Entity>);
            entities = new NativeArray<Entity>(NewGhostIds.Length, Allocator.TempJob);
            if (NewGhostIds.Length > 0)
            {
                EntityManager.CreateEntity(m_InitialArchetype, entities);
                for (var i = 0; i != NewGhostIds.Length; i++)
                {
                    EntityManager.GetBuffer<ReplicatedEntitySerializer>(entities[i]).Reserve(8);
                }
            }

            Profiler.BeginSample("Do Delayed");
            if (m_CurrentDelayedSpawnList.Length > 0)
            {
                var delayedJob = new DelayedSpawnJob
                {
                    entities     = delayedEntities,
                    delayedGhost = m_CurrentDelayedSpawnList,
                    ghostMap     = m_GhostMap
                };
                inputDeps = delayedJob.Schedule(inputDeps);
                inputDeps = m_GhostSerializerGroup.UpdateNewEntities(delayedEntities, m_CurrentDelayedSpawnList, m_GhostMap, inputDeps);
            }
            Profiler.EndSample();

            Profiler.BeginSample("Add to map");
            if (NewGhostIds.Length > 0)
            {
                var job = new AddToGhostMapJob
                {
                    entities          = entities,
                    newGhostIds       = NewGhostIds,
                    ghostMap          = m_ConcurrentGhostMap,
                    pendingSpawnQueue = m_ConcurrentDelayedSpawnQueue
                };
                inputDeps = job.Schedule(entities.Length, 8, inputDeps);
            }
            Profiler.EndSample();

            Profiler.BeginSample("FinalPass");
            inputDeps = m_GhostSerializerGroup.FinalPass(m_GhostMap, inputDeps);
            Profiler.EndSample();
            
            var clearJob = new ClearNewJob
            {
                entities        = entities,
                visibleEntities = delayedEntities,
                newGhostIds     = NewGhostIds,
            };
            Dependency = clearJob.Schedule(inputDeps);

            Profiler.BeginSample("Complete dependency");
            Dependency.Complete();
            Profiler.EndSample();
            
            return Dependency;
        }
    }
}
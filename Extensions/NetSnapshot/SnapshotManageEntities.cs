using package.stormiumteam.networking;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public static class SnapshotManageEntities
    {
        public struct UpdateResult
        {
            public NativeArray<SnapshotEntityInformation> ToCreate;
            public NativeArray<SnapshotEntityInformation> ToDestroy;
        }
        
        [BurstCompile]
        public struct UpdateFromJob : IJob
        {
            public NativeList<SnapshotEntityInformation> toCreateList;
            public NativeList<SnapshotEntityInformation> toDestroyList;

            [ReadOnly]
            public NativeArray<SnapshotEntityInformation> previousArray;
            [ReadOnly]
            public NativeArray<SnapshotEntityInformation> nextArray;

            public void Execute()
            {
                for (var i = 0; i != nextArray.Length; i++)
                {
                    var next = nextArray[i];
                    var ct   = false;
                    for (var j = 0; j != previousArray.Length; j++)
                    {
                        var previous = previousArray[j];
                        if (previous.Source == next.Source)
                        {
                            ct = true;
                            break;
                        }
                    }

                    if (ct)
                        continue;

                    toCreateList.Add(next);
                }

                for (var i = 0; i != previousArray.Length; i++)
                {
                    var previous = previousArray[i];
                    var ct       = false;
                    for (var j = 0; j != nextArray.Length; j++)
                    {
                        var next = nextArray[j];
                        if (previous.Source == next.Source)
                        {
                            ct = true;
                            break;
                        }
                    }

                    if (ct)
                        continue;

                    toDestroyList.Add(previous);
                }
            }
        }

        public static UpdateResult UpdateFrom(NativeArray<SnapshotEntityInformation> previousArray, NativeArray<SnapshotEntityInformation> nextArray, Allocator allocator)
        {
            if (!previousArray.IsCreated)
            {
                return new UpdateResult
                {
                    ToCreate = new NativeArray<SnapshotEntityInformation>(nextArray, allocator)
                };
            }

            using (NativeList<SnapshotEntityInformation> tempListCreate = new NativeList<SnapshotEntityInformation>(nextArray.Length, Allocator.TempJob),
                                                         tempListDestroy = new NativeList<SnapshotEntityInformation>(nextArray.Length, Allocator.TempJob))
            {
                new UpdateFromJob
                {
                    toCreateList  = tempListCreate,
                    toDestroyList = tempListDestroy,
                    nextArray     = nextArray,
                    previousArray = previousArray
                }.Run();

                return new UpdateResult
                {
                    ToCreate  = tempListCreate.ToArray(allocator),
                    ToDestroy = tempListDestroy.ToArray(allocator)
                };
            }
        }

        public static void CreateEntities(UpdateResult result, World world, ref StSnapshotRuntime snapshotRuntime)
        {
            var modelMgr  = world.GetExistingManager<EntityModelManager>();

            foreach (var e in result.ToCreate)
            {
                if (e.ModelId == 0)
                {
                    Debug.Log("Someone feeded us an invalid model!");
                    continue;
                }
                
                Profiler.BeginSample("SpawnEntity");
                var worldEntity = modelMgr.SpawnEntity(e.ModelId, e.Source, snapshotRuntime);
                Profiler.EndSample();
                
                PrivateSet(snapshotRuntime.SnapshotToWorld, e.Source, worldEntity);
                PrivateSet(snapshotRuntime.WorldToSnapshot, worldEntity, e.Source);
                
                //Debug.Log($"creation(w={worldEntity}, o={e.Source}) t={snapshotRuntime.Header.GameTime.Tick}");
            }
        }

        public static void DestroyEntities(UpdateResult entitiesUpdateResult, World world, ref StSnapshotRuntime snapshotRuntime, bool removeLinks = true)
        {
            var entityMgr = world.GetExistingManager<EntityManager>();
            var modelMgr = world.GetExistingManager<EntityModelManager>();
            

            foreach (var e in entitiesUpdateResult.ToDestroy)
            {
                var worldEntity = snapshotRuntime.EntityToWorld(e.Source);
                if (entityMgr.HasComponent<EntitySnapshotManualDestroy>(worldEntity))
                    continue;
                
                if (worldEntity == default || !entityMgr.Exists(worldEntity))
                {
                    Debug.LogError($"Inconsistency when removing entity (w={worldEntity}, o={e.Source}) t={snapshotRuntime.Header.GameTime.Tick}");
                    if (removeLinks)
                    {
                        snapshotRuntime.SnapshotToWorld.Remove(e.Source);
                        snapshotRuntime.WorldToSnapshot.Remove(worldEntity);
                    }
                    continue;
                }
                
                if (removeLinks)
                {
                    snapshotRuntime.SnapshotToWorld.Remove(e.Source);
                    snapshotRuntime.WorldToSnapshot.Remove(worldEntity);
                }
                
                //Debug.Log($"destruction(w={worldEntity}, o={e.Source}) t={snapshotRuntime.Header.GameTime.Tick}");
                
                Profiler.BeginSample("DestroyEntity");
                modelMgr.DestroyEntity(worldEntity, e.ModelId);
                Profiler.EndSample();
            }
        }

        private static void PrivateSet(NativeHashMap<Entity, Entity> hashmap, Entity first, Entity second)
        {
            if (hashmap.TryGetValue(first, out _))
                hashmap.Remove(first);

            hashmap.TryAdd(first, second);
        }
    }
}
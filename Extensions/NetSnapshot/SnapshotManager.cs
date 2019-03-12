using System;
using package.stormiumteam.networking;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public class SnapshotManager : ComponentSystem
    {
        public struct GenerateResult
        {
            public SnapshotRuntime Runtime;
            public DataBufferWriter Data;

            public bool IsCreated => Data.GetSafePtr() != IntPtr.Zero;

            public void Dispose()
            {
                Runtime.Dispose();
                Data.Dispose();
            }
        }

        protected override void OnUpdate()
        {
        }

        [BurstCompile]
        struct TransformEntityArrayJob : IJobParallelFor
        {
            public NativeArray<Entity> EntityArray;
            public NativeArray<SnapshotEntityInformation> Entities;

            [ReadOnly]
            public ComponentDataFromEntity<ModelIdent> Component;
            
            public void Execute(int index)
            {
                Entities[index] = new SnapshotEntityInformation(EntityArray[index], Component[EntityArray[index]].Id);
            }
        }

        public NativeArray<SnapshotEntityInformation> TransformEntityArray(NativeArray<Entity> entityArray, Allocator allocator)
        {
            var entityLength = entityArray.Length;
            var entities     = new NativeArray<SnapshotEntityInformation>(entityLength, allocator);
            
            new TransformEntityArrayJob
            {
                EntityArray = entityArray,
                Entities    = entities,
                
                Component = GetComponentDataFromEntity<ModelIdent>()
            }.Run(entityLength);
            /*for (var i = 0; i != entityLength; i++)
            {
                var e = entityArray[i];
                var m = EntityManager.GetComponentData<ModelIdent>(e);
                
                if (e.Index == 0)
                {
                    Debug.Log($"Some weird things: {entities[i].Source} {entities[i].ModelId}");
                    using (var allEntities = EntityManager.GetAllEntities())
                    {
                        var str = string.Empty;
                        foreach (var entity in allEntities)
                        {
                            str += $"{entity}\n";
                        }
                        Debug.Log(str);
                    }
                }
                
                entities[i] = new SnapshotEntityInformation(e, m.Id);
            }*/

            return entities;
        }

        /*public GenerateResult GenerateLocalSnapshot(NativeArray<Entity> nfEntities, Allocator allocator, SnapshotFlags flags, ref GenerateResult previousResult)
        {
            var entities      = TransformEntityArray(nfEntities, allocator);
            var localReceiver = new SnapshotReceiver(m_LocalClientGroup.GetEntityArray()[0], flags);
            var data          = new DataBufferWriter(allocator, true, 128 + entities.Length * 8);
            var gameTime      = World.GetExistingManager<StGameTimeManager>().GetTimeFromSingleton();
            var result        = GenerateSnapshot(localReceiver, gameTime, entities, allocator, ref data, ref previousResult.Runtime);

            result.Runtime.Entities = entities;

            return result;
        }*/

        public GenerateResult GenerateForConnection(Entity                senderClient,
                                                    Entity                receiverClient,
                                                    NativeArray<Entity>   nfEntities,
                                                    bool                  fullSnapshot,
                                                    int snapshotIdx,
                                                    GameTime              gameTime,
                                                    Allocator             allocator,
                                                    ref DataBufferWriter  data,
                                                    ref SnapshotRuntime previousRuntime)
        {
            var entities = TransformEntityArray(nfEntities, allocator);
            var sender = new SnapshotSender(senderClient, SnapshotFlags.Local);
            var receiver = new SnapshotReceiver(receiverClient, fullSnapshot ? SnapshotFlags.FullData : SnapshotFlags.None);

            return GenerateSnapshot(snapshotIdx, sender, receiver, gameTime, entities, allocator, ref data, ref previousRuntime);
        }
        
        [BurstCompile]
        struct WriteFullEntitiesJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public DataBufferWriter Data;
            public NativeArray<SnapshotEntityInformation> Entities;
            
            public void Execute()
            {
                for (var i = 0; i != Entities.Length; i++)
                {
                    var entity  = Entities[i].Source;
                    var modelId = Entities[i].ModelId;
                
                    Data.WriteDynamicIntWithMask((ulong) entity.Index, (ulong) entity.Version, (ulong) modelId);
                }
            }
        }

        private unsafe void WriteFullEntities(ref DataBufferWriter data, ref NativeArray<SnapshotEntityInformation> entities)
        {
            data.WriteByte((byte) 0);
            data.WriteInt(entities.Length);
            Profiler.BeginSample("WriteFullEntities");
            /*for (var i = 0; i != entities.Length; i++)
            {
                var entity = entities[i].Source;
                var modelId = entities[i].ModelId;
                
                data.WriteDynamicIntWithMask((ulong) entity.Index, (ulong) entity.Version, (ulong) modelId);
            }*/
            new WriteFullEntitiesJob
            {
                Data = data,
                Entities = entities
            }.Run();
            Profiler.EndSample();

            //if (entities.Length > 0) data.WriteDataSafe((byte*) entities.GetUnsafePtr(), entities.Length * sizeof(SnapshotEntityInformation), default);
        }

        private unsafe void WriteIncrementalEntities(ref DataBufferWriter data, ref NativeArray<SnapshotEntityInformation> entities, ref SnapshotRuntime previousRuntime)
        {
            WriteFullEntities(ref data, ref entities);
            return;

            // If 'previousResult' is null or there is no entities on both side, we fallback to writing all entities
            /*if (!previousRuntime.Entities.IsCreated || entities.Length == 0 || previousRuntime.Entities.Length == 0)
            {
                WriteFullEntities(ref data, ref entities);
                return;
            }

            ref var previousEntities = ref previousRuntime.Entities;

            data.Write((byte) 1);

            // -----------------------------------------------------------------
            // -> Removed entities to the buffer
            var removedLengthMarker = data.Write(0);
            var removedLength       = 0;

            // Detect entities that don't exist anymore
            for (var i = 0; i != previousEntities.Length; i++)
            {
                var prev  = previousEntities[i];
                var exist = false;

                for (var j = 0; j != entities.Length; j++)
                {
                    var curr = entities[j];
                    if (prev != curr) continue;

                    exist = true;
                    break;
                }

                if (exist) continue;

                data.Write(ref prev);
                removedLength++;
            }

            data.Write(ref removedLength, removedLengthMarker);

            // -----------------------------------------------------------------
            // -> Added entities to the buffer
            var addedLengthMarker = data.Write(0);
            var addedLength       = 0;

            for (var i = 0; i != entities.Length; i++)
            {
                var curr  = entities[i];
                var exist = false;

                for (var j = 0; j != previousEntities.Length; j++)
                {
                    var prev = previousEntities[j];
                    if (curr != prev) continue;

                    exist = true;
                    break;
                }

                if (exist) continue;

                data.Write(ref curr);
                addedLength++;
            }*/
        }

        public unsafe GenerateResult GenerateSnapshot(int                                    snapshotIdx,
                                                      SnapshotSender                         sender,
                                                      SnapshotReceiver                       receiver,
                                                      GameTime                               gt,
                                                      NativeArray<SnapshotEntityInformation> entities,
                                                      Allocator                              allocator,
                                                      ref DataBufferWriter                   data,
                                                      ref SnapshotRuntime                  runtime)
        {
            IntPtr previousEntityArrayPtr = default;
            Profiler.BeginSample("Create Header");
            var    header                 = new StSnapshotHeader(gt, snapshotIdx, sender);

            runtime.Header = header;
            Profiler.EndSample();
            if (!runtime.Entities.IsCreated)
                runtime.Entities = entities;
            else
                previousEntityArrayPtr = new IntPtr(runtime.Entities.GetUnsafePtr());

            Profiler.BeginSample("Update hashmap");
            runtime.UpdateHashMapFromLocalData();
            
            data.TryResize(data.Length + entities.Length * 4);
            Profiler.EndSample();
            
            // Write Game time
            Profiler.BeginSample("Write Header");
            data.WriteInt(header.SnapshotIdx);
            data.WriteRef(ref gt);
            Profiler.EndSample();

            // Write entity data
            Profiler.BeginSample("Write Entities");
            if ((receiver.Flags & SnapshotFlags.FullData) != 0)
            {
                WriteFullEntities(ref data, ref entities);
            }
            else 
            {
                WriteIncrementalEntities(ref data, ref entities, ref runtime);
            }

            if (runtime.Entities.IsCreated && new IntPtr(runtime.Entities.GetUnsafePtr()) == previousEntityArrayPtr)
            {
                runtime.Entities.Dispose();
                runtime.Entities = entities;
            }

            if (!runtime.Entities.IsCreated)
            {
                Debug.LogWarning("What???");
                runtime.Entities = entities;
            }
            Profiler.EndSample();

            Profiler.BeginSample("SubscribeSystem()");
            foreach (var obj in AppEvent<ISnapshotSubscribe>.GetObjEvents())
                obj.SubscribeSystem();
            Profiler.EndSample();

            var systemsMfc = AppEvent<ISnapshotManageForClient>.GetObjEvents();
            data.WriteInt(systemsMfc.Length);

            // Write system data
            foreach (var obj in systemsMfc)
            {
                var pattern = obj.GetSystemPattern();
                Profiler.BeginSample($"System #{pattern.Id} ({pattern.InternalIdent.Name})");
                Profiler.BeginSample("WriteData");
                var sysData = obj.WriteData(receiver, runtime);
                Profiler.EndSample();

                // We need the latest reference from the buffer data
                // If the buffer get resized, the pointer to the buffer is invalid.
                sysData.UpdateReference();

                // Used for skipping data when reading
                Profiler.BeginSample("Other...");
                data.WriteDynamicIntWithMask((ulong) pattern.Id, (ulong) sysData.Length);
                data.WriteBuffer(sysData);
                Profiler.EndSample();
                sysData.Dispose();
                Profiler.EndSample();
            }

            return new GenerateResult {Data = data, Runtime = runtime};
        }

        private static ISnapshotManageForClient GetSystem(int id)
        {
            foreach (var system in AppEvent<ISnapshotManageForClient>.GetObjEvents())
            {
                if (system.GetSystemPattern() == id) return system;
            }

            return null;
        }
        
        public unsafe SnapshotRuntime ApplySnapshotFromData(SnapshotSender sender, ref DataBufferReader data, ref SnapshotRuntime previousRuntime, PatternBankExchange exchange)
        {
            // Terminate the function if the runtime is bad.
            if (previousRuntime.Allocator == Allocator.None || previousRuntime.Allocator == Allocator.Invalid)
            {
                throw new Exception($"{nameof(previousRuntime.Allocator)} is set as None or Invalid. This may be caused by a non defined or corrupted runtime.");
            }

            // --------------------------------------------------------------------------- //
            // Read Only Data...
            var allocator = previousRuntime.Allocator;

            // --------------------------------------------------------------------------- //
            // Actual code
            // --------------------------------------------------------------------------- //
            var snapshotIdx = data.ReadValue<int>();
            var gameTime    = data.ReadValue<GameTime>();

            var header  = new StSnapshotHeader(gameTime, snapshotIdx, sender);
            var runtime = new SnapshotRuntime(header, previousRuntime, allocator);

            // Read Entity Data
            SnapshotManageEntities.UpdateResult entitiesUpdateResult = default;

            var entityDataType = data.ReadValue<byte>();
            switch (entityDataType)
            {
                // Full data
                case 0:
                {
                    Profiler.BeginSample("ReadFullEntities");
                    ReadFullEntities(out var tempEntities, ref data, ref allocator, exchange);
                    Profiler.EndSample();
                    Profiler.BeginSample("UpdateFrom");
                    entitiesUpdateResult = SnapshotManageEntities.UpdateFrom(previousRuntime.Entities, tempEntities, allocator);
                    Profiler.EndSample();
                    
                    if (runtime.Entities.IsCreated)
                        runtime.Entities.Dispose();
                    runtime.Entities = tempEntities;

                    break;
                }
                case 1:
                {
                    break;
                }
                default:
                {
                    throw new Exception("Exception when reading.");
                }
            }

            Profiler.BeginSample("CreateEntities");
            SnapshotManageEntities.CreateEntities(entitiesUpdateResult, World, ref runtime);
            Profiler.EndSample();
            Profiler.BeginSample("DestroyEntities");
            SnapshotManageEntities.DestroyEntities(entitiesUpdateResult, World, ref runtime, true);
            Profiler.EndSample();
            
            foreach (var obj in AppEvent<ISnapshotSubscribe>.GetObjEvents())
                obj.SubscribeSystem();

            // Read System Data
            var systemLength = data.ReadValue<int>();
            Profiler.BeginSample("Read System Data");

            for (var i = 0; i != systemLength; i++)
            {
                data.ReadDynIntegerFromMask(out var uForeignSystemPattern, out var uLength);

                var foreignSystemPattern = (int) uForeignSystemPattern;
                var length               = (int) uLength;
                var system               = GetSystem(exchange.GetOriginId(foreignSystemPattern));

                Profiler.BeginSample($"Read From System {system.GetSystemPattern().InternalIdent.Name} #" + system.GetSystemPattern().Id);
                system.ReadData(sender, runtime, new DataBufferReader(data, data.CurrReadIndex, data.CurrReadIndex + length));
                Profiler.EndSample();

                data.CurrReadIndex += length;
            }
            Profiler.EndSample();
            return runtime;
        }

        private unsafe void ReadFullEntities(out NativeArray<SnapshotEntityInformation> entities, ref DataBufferReader data, ref Allocator allocator, PatternBankExchange exchange)
        {
            var entityLength = data.ReadValue<int>();
            if (entityLength <= 0) Debug.LogWarning("No entities.");

            entities = new NativeArray<SnapshotEntityInformation>(entityLength, allocator);
            //UnsafeUtility.MemCpy(entities.GetUnsafePtr(), data.DataPtr + data.CurrReadIndex, entityLength * sizeof(SnapshotEntityInformation));

            for (var i = 0; i != entityLength; i++)
            {
                data.ReadDynIntegerFromMask(out var uEntityIndex, out var uEntityVersion, out var uModelId);

                var entity  = new Entity {Index = (int) uEntityIndex, Version = (int) uEntityVersion};
                var modelId = (int) uModelId;

                if (modelId == 0)
                {
                    Debug.LogError("Error with model of " + entity);
                    entities[i] = new SnapshotEntityInformation(entity, 0);
                    continue;
                }

                var localModel = exchange.GetOriginId(modelId);
                if (localModel == 0)
                {
                    Debug.Log($"Local model is zero while the server model is {modelId}");
                    continue;
                }

                entities[i] = new SnapshotEntityInformation(entity, localModel);
            }
        }
    }
}
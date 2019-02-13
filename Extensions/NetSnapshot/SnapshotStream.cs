using System;
using Unity.Collections;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    public enum SkipReason : byte
    {
        None,
        NoDeltaDifference
    }

    public static class SnapshotOutputUtils
    {        
        public static bool ShouldSkip(SnapshotReceiver receiver, SkipReason skipReason)
        {
            if ((receiver.Flags & SnapshotFlags.FullData) != 0) return false;
            
            return skipReason != SkipReason.None;
        }

        public static bool ShouldSkip<T>(SnapshotReceiver receiver, DataChanged<T> changed)
            where T : struct, IComponentData
        {
            return ShouldSkip(receiver, changed.IsDirty == 0 ? SkipReason.NoDeltaDifference : SkipReason.None);
        }
    }

    public struct StSnapshotRuntime
    {
        public Allocator Allocator;

        public StSnapshotHeader Header;

        [ReadOnly]
        public NativeArray<SnapshotEntityInformation> Entities;
        [ReadOnly]
        public NativeHashMap<Entity, Entity> SnapshotToWorld;
        [ReadOnly]
        public NativeHashMap<Entity, Entity> WorldToSnapshot;
        
        public StSnapshotRuntime(StSnapshotHeader header, StSnapshotRuntime previousRuntime, Allocator wantedAllocator)
        {
            if (previousRuntime.Allocator != wantedAllocator) throw new Exception();

            Allocator = wantedAllocator;
            Header = header;

            Entities        = default;
            SnapshotToWorld = previousRuntime.SnapshotToWorld;
            WorldToSnapshot = previousRuntime.WorldToSnapshot;
        }
        
        public StSnapshotRuntime(StSnapshotHeader header, Allocator allocator)
        {
            Allocator = allocator;

            Header = header;

            Entities = default;
            SnapshotToWorld = new NativeHashMap<Entity, Entity>(128, allocator);
            WorldToSnapshot = new NativeHashMap<Entity, Entity>(128, allocator);
        }
        
        public Entity EntityToWorld(Entity snapshotEntity)
        {
            SnapshotToWorld.TryGetValue(snapshotEntity, out var worldEntity);

            return worldEntity;
        }

        public Entity EntityToSnapshot(Entity worldEntity)
        {
            WorldToSnapshot.TryGetValue(worldEntity, out var snapshotEntity);

            return snapshotEntity;
        }
        
        public Entity GetWorldEntityFromCustom(NativeArray<Entity> entities, int systemIndex)
        {
            return EntityToWorld(entities[systemIndex]);
        }

        public Entity GetWorldEntityFromGlobal(int index)
        {
            return EntityToWorld(Entities[index].Source);
        }

        public NativeList<(Entity worldRemoved, Entity snapshotRemoved)> UpdateHashMap()
        {
            var list = new NativeList<(Entity worldRemoved, Entity snapshotRemoved)>(SnapshotToWorld.Length, Allocator.Temp);
            
            // Remove data from old <Entity>To<Entity> HashMap
            for (var i = 0; i != Entities.Length; i++)
            {
                var entity = Entities[i];
                if (SnapshotToWorld.TryGetValue(entity.Source, out var worldEntity))
                    continue;
                
                WorldToSnapshot.Remove(worldEntity);
                SnapshotToWorld.Remove(entity.Source);
                    
                list.Add((worldEntity, entity.Source));
            }

            return list;
        }
        
        public void UpdateHashMapFromLocalData()
        {
            SnapshotToWorld.Clear();
            WorldToSnapshot.Clear();
            
            for (var i = 0; i != Entities.Length; i++)
            {
                var e = Entities[i];
                SnapshotToWorld.TryAdd(e.Source, e.Source);
                WorldToSnapshot.TryAdd(e.Source, e.Source);
            }
        }

        public void Dispose()
        {
            Entities.Dispose();
            SnapshotToWorld.Dispose();
            WorldToSnapshot.Dispose();
        }
    }

    public struct StSnapshotHeader
    {
        public GameTime GameTime;
        public int SnapshotIdx;
        public SnapshotSender Sender;

        public StSnapshotHeader(GameTime gameTime, int snapshotIdx, SnapshotSender sender)
        {
            GameTime = gameTime;
            SnapshotIdx = snapshotIdx;
            Sender = sender;
        }
    }
}
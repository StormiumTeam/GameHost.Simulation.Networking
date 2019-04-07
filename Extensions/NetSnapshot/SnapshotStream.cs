using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StormiumShared.Core.Networking
{
    public enum SkipReason : byte
    {
        None,
        NoDeltaDifference
    }

    public static class SnapshotOutputUtils
    {        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldSkip(SnapshotReceiver receiver, SkipReason skipReason)
        {
            if ((receiver.Flags & SnapshotFlags.FullData) != 0) return false;
            
            return skipReason != SkipReason.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldSkip<T>(SnapshotReceiver receiver, DataChanged<T> changed)
            where T : struct, IComponentData
        {
            return math.select(0, math.select(1, 0, changed.IsDirty), (receiver.Flags & SnapshotFlags.FullData) == 0) == 1;
        }
    }

    public struct SnapshotRuntime
    {
        public Allocator Allocator;

        public StSnapshotHeader Header;

        [ReadOnly]
        public NativeArray<SnapshotEntityInformation> Entities;
        [ReadOnly]
        public NativeHashMap<Entity, Entity> SnapshotToWorld;
        [ReadOnly]
        public NativeHashMap<Entity, Entity> WorldToSnapshot;
        
        public SnapshotRuntime(StSnapshotHeader header, SnapshotRuntime previousRuntime, Allocator wantedAllocator)
        {
            if (previousRuntime.Allocator != wantedAllocator) throw new Exception();

            Allocator = wantedAllocator;
            Header = header;

            Entities        = default;
            SnapshotToWorld = previousRuntime.SnapshotToWorld;
            WorldToSnapshot = previousRuntime.WorldToSnapshot;
        }
        
        public SnapshotRuntime(StSnapshotHeader header, Allocator allocator)
        {
            Allocator = allocator;

            Header = header;

            Entities = default;
            SnapshotToWorld = new NativeHashMap<Entity, Entity>(128, allocator);
            WorldToSnapshot = new NativeHashMap<Entity, Entity>(128, allocator);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity EntityToWorld(Entity snapshotEntity)
        {
            SnapshotToWorld.TryGetValue(snapshotEntity, out var worldEntity);

            return worldEntity;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity EntityToSnapshot(Entity worldEntity)
        {
            WorldToSnapshot.TryGetValue(worldEntity, out var snapshotEntity);

            return snapshotEntity;
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Entity GetWorldEntityFromCustom(NativeArray<Entity> entities, int systemIndex)
        {
            return EntityToWorld(entities[systemIndex]);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        public EntityFilterModelEnumerator GetEntityFilterModelEnumerator(int modelId)
        {
            return new EntityFilterModelEnumerator(Entities, modelId);
        }

        public void Dispose()
        {
            Entities.Dispose();
            SnapshotToWorld.Dispose();
            WorldToSnapshot.Dispose();
        }

        public int GetIndex(Entity entity)
        {
            var l = Entities.Length;
            for (var i = 0; i != l; i++)
            {
                if (Entities[i].Source == entity)
                    return i;
            }

            return -1;
        }
    }

    public struct EntityFilterModelEnumerator : IEnumerator<SnapshotEntityInformation>
    {
        private NativeArray<SnapshotEntityInformation> m_Entities;
        private int m_CurrentIndex;
        private int m_FilterIndex;
        private int m_ModelId;
        
        public bool MoveNext()
        {
            while (m_Entities[m_CurrentIndex].ModelId != m_ModelId)
            {
                m_CurrentIndex++;
                if (m_CurrentIndex >= m_Entities.Length)
                    return false;
            }

            Current = m_Entities[m_CurrentIndex];
            m_FilterIndex++;
            return true;
        }

        public void Reset()
        {
            m_CurrentIndex = 0;
        }

        public SnapshotEntityInformation Current { get; private set; }
        public int EntitiesIndex => m_CurrentIndex;
        public int FilterIndex => m_FilterIndex;

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }

        public EntityFilterModelEnumerator(NativeArray<SnapshotEntityInformation> entities, int modelId)
        {
            m_Entities = entities;
            m_CurrentIndex = 0;
            m_ModelId = modelId;
            m_FilterIndex = -1;

            Current = default;
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
using System;
using Collections.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{
	public struct SerializeClientData : IDisposable
	{
		internal NativeHashMap<uint, IntPtr> m_GhostSnapshots;

		public bool                    Created;
		public NetworkCompressionModel NetworkCompressionModel;
		public NativeList<uint>        ProgressiveGhostIds;
		public NativeList<uint>        BlockedGhostIds;
		public NativeList<uint>        KnownArchetypes;

		public uint Tick;

		[ReadOnly]
		public NativeArray<ArchetypeChunk> AllChunks;

		[ReadOnly]
		public ArchetypeChunkComponentType<GhostIdentifier> GhostType;

		[ReadOnly]
		public ArchetypeChunkEntityType EntityType;

		public Entity Client;

		public SerializeClientData(Allocator allocator)
		{
			if (allocator != Allocator.Persistent)
				throw new InvalidOperationException("Only persistent allocators are accepted");

			Created = true;

			// Those variables are assigned automatically by a system
			AllChunks  = default;
			GhostType  = default;
			EntityType = default;
			// >

			m_GhostSnapshots        = new NativeHashMap<uint, IntPtr>(128, Allocator.Persistent);
			NetworkCompressionModel = new NetworkCompressionModel(Allocator.Persistent);
			ProgressiveGhostIds     = new NativeList<uint>(Allocator.Persistent);
			BlockedGhostIds         = new NativeList<uint>(Allocator.Persistent);
			KnownArchetypes         = new NativeList<uint>(Allocator.Persistent);

			Client = default;
			Tick   = 0;
		}

		public void BeginSerialize(ComponentSystemBase system, NativeArray<ArchetypeChunk> chunks)
		{
			AllChunks  = chunks;
			GhostType  = system.GetArchetypeChunkComponentType<GhostIdentifier>();
			EntityType = system.GetArchetypeChunkEntityType();
		}

		internal unsafe void CreateSnapshotFor(uint ghostId)
		{
			var     ptr  = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<GhostSnapshot>(), UnsafeUtility.AlignOf<GhostSnapshot>(), Allocator.Persistent);
			ref var data = ref UnsafeUtilityEx.AsRef<GhostSnapshot>(ptr);
			data.Id         = ghostId;
			data.SystemData = UnsafeHashMap.Allocate<uint, IntPtr>(256);

			m_GhostSnapshots[ghostId] = (IntPtr) ptr;
		}

		public unsafe bool TryGetSnapshot(uint ghostId, out GhostSnapshot snapshot)
		{
			if (m_GhostSnapshots.TryGetValue(ghostId, out var snapshotPtr))
			{
				UnsafeUtility.CopyPtrToStructure((void*) snapshotPtr, out snapshot);
				return true;
			}

			snapshot = default;
			return false;
		}

		public unsafe void Dispose()
		{
			foreach (var value in m_GhostSnapshots.GetValueArray(Allocator.Temp))
			{
				ref var data = ref UnsafeUtilityEx.AsRef<GhostSnapshot>(value.ToPointer());
				data.Dispose();

				UnsafeUtility.Free(value.ToPointer(), Allocator.Persistent);
			}

			m_GhostSnapshots.Dispose();
			NetworkCompressionModel.Dispose();
			ProgressiveGhostIds.Dispose();
			BlockedGhostIds.Dispose();
			KnownArchetypes.Dispose();
		}
	}
}
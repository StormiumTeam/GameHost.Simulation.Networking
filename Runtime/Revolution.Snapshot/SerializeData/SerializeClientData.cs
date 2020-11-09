using System;
using System.Runtime.CompilerServices;
using Collections.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution
{
	public struct SerializeClientData : IDisposable
	{
		internal NativeHashMap<uint, IntPtr> GhostSnapshots;

		public bool                    Created;
		public NetworkCompressionModel NetworkCompressionModel;
		public NativeList<uint>        ProgressiveGhostIds;
		public NativeList<uint>        BlockedGhostIds;
		public NativeList<uint>        KnownArchetypes;

		public uint Tick;

		[ReadOnly]
		public NativeArray<ArchetypeChunk> AllChunks;

		[ReadOnly]
		public ComponentTypeHandle<GhostIdentifier> GhostType;

		[ReadOnly]
		public EntityTypeHandle EntityType;

		public Entity Client;

		private NativeArray<IntPtr> m_GhostSnapshots;

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

			GhostSnapshots        = new NativeHashMap<uint, IntPtr>(128, Allocator.Persistent);
			NetworkCompressionModel = new NetworkCompressionModel(Allocator.Persistent);
			ProgressiveGhostIds     = new NativeList<uint>(Allocator.Persistent);
			BlockedGhostIds         = new NativeList<uint>(Allocator.Persistent);
			KnownArchetypes         = new NativeList<uint>(Allocator.Persistent);

			Client = default;
			Tick   = 0;

			m_GhostSnapshots = default;
		}

		public void BeginSerialize(ComponentSystemBase system, NativeArray<ArchetypeChunk> chunks)
		{
			AllChunks  = chunks;
			GhostType  = system.GetComponentTypeHandle<GhostIdentifier>();
			EntityType = system.GetEntityTypeHandle();
		}

		internal unsafe void CreateSnapshotFor(uint ghostId)
		{
			var     ptr  = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<GhostSnapshot>(), UnsafeUtility.AlignOf<GhostSnapshot>(), Allocator.Persistent);
			ref var data = ref UnsafeUtility.AsRef<GhostSnapshot>(ptr);
			data.Id         = ghostId;
			data.Allocate();

			GhostSnapshots[ghostId] = (IntPtr) ptr;

			var prevLength = m_GhostSnapshots.IsCreated ? m_GhostSnapshots.Length : 0;
			if (m_GhostSnapshots.IsCreated)
				m_GhostSnapshots.Dispose();
			
			m_GhostSnapshots = new NativeArray<IntPtr>((int) math.max(prevLength, ghostId + 1), Allocator.Persistent, NativeArrayOptions.ClearMemory);
			for (var i = 0; i != m_GhostSnapshots.Length; i++)
			{
				if (!GhostSnapshots.TryGetValue((uint) i, out var snapPtr))
					continue;

				m_GhostSnapshots[i] = snapPtr;
			}
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe bool TryGetSnapshot(uint ghostId, out GhostSnapshot snapshot)
		{
			if (GhostSnapshots.TryGetValue(ghostId, out var snapshotPtr))
			{
				UnsafeUtility.CopyPtrToStructure((void*) snapshotPtr, out snapshot);
				return true;
			}

			snapshot = default;
			return false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public unsafe ref GhostSnapshot GetSnapshot(uint ghostId)
		{
			//return UnsafeUtilityEx.AsRef<GhostSnapshot>((void*) GhostSnapshots[ghostId]);
			return ref UnsafeUtility.AsRef<GhostSnapshot>((void*) m_GhostSnapshots[(int) ghostId]);
		}

		public unsafe void Dispose()
		{
			foreach (var value in GhostSnapshots.GetValueArray(Allocator.Temp))
			{
				ref var data = ref UnsafeUtility.AsRef<GhostSnapshot>(value.ToPointer());
				data.Dispose();

				UnsafeUtility.Free(value.ToPointer(), Allocator.Persistent);
			}

			GhostSnapshots.Dispose();
			NetworkCompressionModel.Dispose();
			ProgressiveGhostIds.Dispose();
			BlockedGhostIds.Dispose();
			KnownArchetypes.Dispose();
			m_GhostSnapshots.Dispose();
		}
	}
}
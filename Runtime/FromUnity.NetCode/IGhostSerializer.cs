using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Unity.NetCode
{
	public struct GhostComponent : IComponentData
	{
	}

	public delegate bool d_CanSerialize(ref GhostSerializerBase serializer, EntityArchetype arch);

	public delegate void d_BeginSerialize(ref GhostSerializerBase serializer, GCHandle systemHandle);

	public delegate void d_BeginDeserialize(ref GhostSerializerBase serializer, GCHandle jobSystemHandle);

	public unsafe delegate void d_Spawn(ref GhostSerializerBase serializer, int ghostId, uint tick, void* reader, void* readerCtx, void* compressionModel);

	public unsafe delegate bool d_HasComponent(ref GhostSerializerBase serializer, Entity entity);

	public unsafe delegate void d_CopyToSnapshot(ref GhostSerializerBase serializer, ArchetypeChunk chunk, int ent, uint currentTick, void* snapshot);

	// ?????????????????????
	// I originally wanted to put the real types for writer and compressionModel, but for some weird reason, I had a MarshalDirectiveException????????
	public unsafe delegate void d_SerializeEntity(ref GhostSerializerBase serializer, void* snapshot, void* baseline, void* writer, void* compressionModel);

	public unsafe delegate void d_DeserializeEntity(ref GhostSerializerBase serializer, void* snapshot, uint tick, void* baseline, void* reader, void* readerCtx, void* compressionModel);

	public unsafe delegate void d_FullDeserializeEntity(ref GhostSerializerBase serializer, Entity entity, uint tick, uint baselineTick, uint baselineTick2, uint baselineTick3, void* reader, void* readerCtx,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
	                                                    AtomicSafetyHandle bufferSafetyHandle,
#endif
	                                                    void* compressionModel);

	public unsafe delegate void d_PredictDelta(ref GhostSerializerBase serializer, uint tick, void* baseline, void* baseline1, void* baseline2);

	public delegate void d_SetupDeserializing(ref GhostSerializerBase serializer, GCHandle jobSystemHandle);

	public unsafe struct GhostSerializerBase
	{
		public ref GhostSerializerHeader Header => ref UnsafeUtilityEx.AsRef<GhostSerializerHeader>(UnsafeUtility.AddressOf(ref this));

		public ref TSerializer As<TSerializer>()
			where TSerializer : struct, IGhostSerializer
		{
			return ref UnsafeUtilityEx.AsRef<TSerializer>(UnsafeUtility.AddressOf(ref this));
		}
	}

	public unsafe struct GhostSerializerHeader
	{
		public byte Flags;

		public bool WantsPredictionDelta;
		public bool WantsSingleHistory;

		public int Importance;
		public int SnapshotSize;
		public int SnapshotAlign;

		public int Size;
		public int Align;

		public FunctionPointer<d_CanSerialize>          CanSerializeFunc;
		public FunctionPointer<d_BeginSerialize>        BeginSerializeFunc;
		public FunctionPointer<d_BeginDeserialize>      BeginDeserializeFunc;
		public FunctionPointer<d_Spawn>                 SpawnFunc;
		public FunctionPointer<d_HasComponent>          HasComponentFunc;
		public FunctionPointer<d_CopyToSnapshot>        CopyToSnapshotFunc;
		public FunctionPointer<d_SerializeEntity>       SerializeEntityFunc;
		public FunctionPointer<d_DeserializeEntity>     DeserializeEntityFunc;
		public FunctionPointer<d_PredictDelta>          PredictDeltaFunc;
		public FunctionPointer<d_FullDeserializeEntity> FullDeserializeEntityFunc;
		public FunctionPointer<d_SetupDeserializing>    SetupDeserializingFunc;

		public void* SnapshotFromEntity;
		public int   Id;

		public bool IsValid => Flags != 0;

		public static void Override<TSerializer>(ref TSerializer serializer,
		                                         bool            wantsPredictionDelta = false,
		                                         int             importance           = 0,
		                                         int?            size                 = null)
			where TSerializer : struct, IGhostSerializer
		{
			ref var header = ref GetHeader(ref serializer);

			if (header.Flags == 0)
				throw new InvalidOperationException("this serializer was not initialized.");

			header.WantsPredictionDelta = wantsPredictionDelta;
			header.Importance           = importance;
			header.Size                 = size ?? header.Size;

			if (header.Size == 0)
			{
				throw new InvalidOperationException("size is 0.");
			}
		}

		public static ref GhostSerializerHeader GetHeader<TSerializer>(ref TSerializer serializer)
			where TSerializer : struct, IGhostSerializer
		{
			return ref UnsafeUtilityEx.AsRef<GhostSerializerHeader>(UnsafeUtility.AddressOf(ref serializer));
		}

		public static void SetHeader<TSerializer>(ref TSerializer serializer, GhostSerializerHeader header)
			where TSerializer : struct, IGhostSerializer
		{
			UnsafeUtility.MemCpy(UnsafeUtility.AddressOf(ref serializer), UnsafeUtility.AddressOf(ref header), UnsafeUtility.SizeOf<GhostSerializerHeader>());
		}
	}

	public interface IGhostSerializer
	{
		GhostSerializerHeader Header { get; }
	}

	public interface IGhostSerializer<T> : IGhostSerializer
		where T : unmanaged, ISnapshotData<T>
	{
		void SetupHeader(ComponentSystemBase system, ref GhostSerializerHeader header);

		void BeginSerialize(ComponentSystemBase  system);
		void BeginDeserialize(JobComponentSystem system);
		void Spawn(int                           ghostId, T data);
		bool CanSerialize(EntityArchetype        arch);
		void CopyToSnapshot(ArchetypeChunk       chunk, int ent, uint tick, ref T snapshot);
	}
}
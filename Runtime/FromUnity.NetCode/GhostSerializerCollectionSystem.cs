using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Unity.NetCode
{
	public unsafe struct GhostSerializerReference : IDisposable
	{
		private Allocator m_Allocator;

		public GhostSerializerBase* Value;

		public GhostSerializerReference(GhostSerializerHeader header, Allocator allocator)
		{
			m_Allocator = allocator;
			Value       = (GhostSerializerBase*) UnsafeUtility.Malloc(header.Size, header.Align, allocator);
			
			UnsafeUtility.MemCpy(Value, UnsafeUtility.AddressOf(ref header), header.Size);
		}

		public void Dispose()
		{
			UnsafeUtility.Free(Value, m_Allocator);
		}

		public ref GhostSerializerBase AsRef()
		{
			return ref UnsafeUtilityEx.AsRef<GhostSerializerBase>(Value);
		}
	}

	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	public unsafe class GhostSerializerCollectionSystem : ComponentSystem
	{
		private NativeList<GhostSerializerHeader> m_Headers;
		private NativeHashMap<int, int>           m_TypeIndexToHeader;

		private static class Functions<TSerializer, TSnapshotData>
			where TSerializer : struct, IGhostSerializer<TSnapshotData>
			where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		{
			[BurstCompile]
			public static bool CanSerialize(ref GhostSerializerBase s, EntityArchetype arch)
			{
				return s.As<TSerializer>().CanSerialize(arch);
			}

			[BurstCompile]
			public static void BeginSerialize(ref GhostSerializerBase s, GCHandle systemHandle)
			{
				var system = systemHandle.Target;
				
				s.As<TSerializer>().BeginSerialize((ComponentSystemBase) system);
			}

			[BurstCompile]
			public static void CopyToSnapshot(ref GhostSerializerBase s, ArchetypeChunk chunk, int ent, uint tick, void* snapshot)
			{
				ref var managedSnapshot = ref UnsafeUtilityEx.AsRef<TSnapshotData>(snapshot);
				s.As<TSerializer>().CopyToSnapshot(chunk, ent, tick, ref managedSnapshot);
			}

			[BurstCompile]
			public static void SerializeEntity(ref GhostSerializerBase s, void* snapshot, void* baseline, void* writer, void* compressionModel)
			{
				ref var managedSnapshot = ref UnsafeUtilityEx.AsRef<TSnapshotData>(snapshot);
				ref var managedBaseline = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline);

				UnsafeUtility.CopyPtrToStructure(writer, out DataStreamWriter w);
				UnsafeUtility.CopyPtrToStructure(compressionModel, out NetworkCompressionModel c);
				
				managedSnapshot.Serialize(ref managedBaseline, w, c);
			}

			public static unsafe void PredictDelta(ref GhostSerializerBase serializer, uint tick, void* baseline, void* baseline1, void* baseline2)
			{
				ref var managedBaseline  = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline);
				ref var managedBaseline1 = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline1);
				ref var managedBaseline2 = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline2);

				managedBaseline.PredictDelta(tick, ref managedBaseline1, ref managedBaseline2);
			}
		}

		protected override void OnCreate()
		{
			base.OnCreate();

			m_Headers           = new NativeList<GhostSerializerHeader>(128, Allocator.Persistent);
			m_TypeIndexToHeader = new NativeHashMap<int, int>(128, Allocator.Persistent);
		}

		protected override void OnUpdate()
		{

		}

		public bool TryAdd<TSerializer, TSnapshotData>(out TSerializer serializer)
			where TSerializer : struct, IGhostSerializer<TSnapshotData>
			where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		{
			var typeIndex = ComponentType.ReadWrite<TSnapshotData>().TypeIndex;
			var item      = -1;
			if (m_TypeIndexToHeader.TryGetValue(typeIndex, out item))
			{
				if (item < 0)
					throw new InvalidOperationException();

				serializer = default(TSerializer);
				GhostSerializerHeader.SetHeader(ref serializer, m_Headers[item]);

				return false;
			}

			serializer = new TSerializer();
			ref var header = ref GhostSerializerHeader.GetHeader(ref serializer);

			header.SnapshotSize        = UnsafeUtility.SizeOf<TSnapshotData>();
			header.SnapshotAlign       = UnsafeUtility.AlignOf<TSnapshotData>();
			header.Size                = UnsafeUtility.SizeOf<TSerializer>();
			header.Align               = UnsafeUtility.AlignOf<TSerializer>();
			header.CanSerializeFunc    = BurstCompiler.CompileFunctionPointer<d_CanSerialize>(Functions<TSerializer, TSnapshotData>.CanSerialize);
			header.BeginSerializeFunc  = new FunctionPointer<d_BeginSerialize>(Marshal.GetFunctionPointerForDelegate<d_BeginSerialize>(Functions<TSerializer, TSnapshotData>.BeginSerialize));
			header.CopyToSnapshotFunc  = BurstCompiler.CompileFunctionPointer<d_CopyToSnapshot>(Functions<TSerializer, TSnapshotData>.CopyToSnapshot);
			header.SerializeEntityFunc = BurstCompiler.CompileFunctionPointer<d_SerializeEntity>(Functions<TSerializer, TSnapshotData>.SerializeEntity);
			header.PredictDeltaFunc    = BurstCompiler.CompileFunctionPointer<d_PredictDelta>(Functions<TSerializer, TSnapshotData>.PredictDelta);

			m_Headers.Add(header);
			if (!m_TypeIndexToHeader.TryAdd(typeIndex, m_Headers.Length - 1))
			{
				throw new InvalidOperationException();
			}

			return true;
		}

		public NativeArray<GhostSerializerHeader> GetHeaderCollection()
		{
			return m_Headers.AsArray();
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			m_Headers.Dispose();
			m_TypeIndexToHeader.Dispose();
		}

		public NativeArray<GhostSerializerReference> BeginSerialize(ComponentSystemBase system, Allocator allocator)
		{
			var referenceArray = new NativeArray<GhostSerializerReference>(m_Headers.Length, allocator);
			var systemHandle = GCHandle.Alloc(system, GCHandleType.Pinned);
			for (var i = 0; i != m_Headers.Length; i++)
			{
				referenceArray[i] = new GhostSerializerReference(m_Headers[i], allocator);
				m_Headers[i].BeginSerializeFunc.Invoke(ref referenceArray[i].AsRef(), systemHandle);
			}
			systemHandle.Free();

			return referenceArray;
		}
	}
}
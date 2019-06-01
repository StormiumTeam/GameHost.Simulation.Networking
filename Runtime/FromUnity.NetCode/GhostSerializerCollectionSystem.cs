using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

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
	public unsafe class GhostSerializerCollectionSystem : JobComponentSystem
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
			public static void BeginDeserialize(ref GhostSerializerBase s, GCHandle systemHandle)
			{
				var system = systemHandle.Target;

				s.As<TSerializer>().BeginDeserialize((JobComponentSystem) system);
			}

			[BurstCompile]
			public static void SetupDeserializing(ref GhostSerializerBase s, GCHandle systemHandle)
			{
				var     system             = (JobComponentSystem) systemHandle.Target;
				var     snapshotFromEntity = system.GetBufferFromEntity<TSnapshotData>();
				ref var header             = ref s.Header;

				UnsafeUtility.MemCpy(header.SnapshotFromEntity, UnsafeUtility.AddressOf(ref snapshotFromEntity), UnsafeUtility.SizeOf<BufferFromEntity<TSnapshotData>>());
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


			[BurstCompile]
			public static void DeserializeEntity(ref GhostSerializerBase s, void* snapshot, uint tick, void* baseline, void* reader, void* readerCtx, void* compressionModel)
			{
				ref var managedSnapshot = ref UnsafeUtilityEx.AsRef<TSnapshotData>(snapshot);
				ref var managedBaseline = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline);

				UnsafeUtility.CopyPtrToStructure(reader, out DataStreamReader r);
				UnsafeUtility.CopyPtrToStructure(compressionModel, out NetworkCompressionModel c);

				ref var ctx = ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(readerCtx);

				managedSnapshot.Deserialize(tick, ref managedBaseline, r, ref ctx, c);
			}

			public static unsafe void PredictDelta(ref GhostSerializerBase serializer, uint tick, void* baseline, void* baseline1, void* baseline2)
			{
				ref var managedBaseline  = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline);
				ref var managedBaseline1 = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline1);
				ref var managedBaseline2 = ref UnsafeUtilityEx.AsRef<TSnapshotData>(baseline2);

				managedBaseline.PredictDelta(tick, ref managedBaseline1, ref managedBaseline2);
			}

			public static unsafe void FullDeserializeEntity(ref GhostSerializerBase serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3, void* reader, void* readerCtx, void* compressionModel)
			{
				UnsafeUtility.CopyPtrToStructure(reader, out DataStreamReader r);
				UnsafeUtility.CopyPtrToStructure(compressionModel, out NetworkCompressionModel c);

				var bfeSafetyHandle = AtomicSafetyHandle.Create(); // super wrong!
				var arraySafetyHandle = AtomicSafetyHandle.Create(); // super wrong!
				
				UnsafeUtility.MemCpy(serializer.Header.SnapshotFromEntity,
					UnsafeUtility.AddressOf(ref bfeSafetyHandle),
					UnsafeUtility.SizeOf<AtomicSafetyHandle>());
				
				UnsafeUtility.MemCpy((byte*)serializer.Header.SnapshotFromEntity + UnsafeUtility.SizeOf<AtomicSafetyHandle>(),
					UnsafeUtility.AddressOf(ref arraySafetyHandle),
					UnsafeUtility.SizeOf<AtomicSafetyHandle>());
				
				ref var ctx                = ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(readerCtx);
				ref var snapshotFromEntity = ref UnsafeUtilityEx.AsRef<BufferFromEntity<TSnapshotData>>(serializer.Header.SnapshotFromEntity);

				var snapshotArray = snapshotFromEntity[entity];
				var baselineData  = default(TSnapshotData);
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
					var baselineData2 = default(TSnapshotData);
					var baselineData3 = default(TSnapshotData);
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

				var data = default(TSnapshotData);
				data.Deserialize(snapshot, ref baselineData, r, ref ctx, c);
				// Replace the oldest snapshot, or add a new one
				if (snapshotArray.Length == GhostSendSystem.SnapshotHistorySize)
					snapshotArray.RemoveAt(0);
				snapshotArray.Add(data);
				
				AtomicSafetyHandle.Release(bfeSafetyHandle);
				AtomicSafetyHandle.Release(arraySafetyHandle);
			}

			public static void Spawn(ref GhostSerializerBase serializer, int ghostId, uint snapshot, void* reader, void* readerCtx, void* compressionModel)
			{
				UnsafeUtility.CopyPtrToStructure(reader, out DataStreamReader r);
				UnsafeUtility.CopyPtrToStructure(compressionModel, out NetworkCompressionModel c);

				ref var ctx = ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(readerCtx);

				var snapshotData = default(TSnapshotData);
				var baselineData = default(TSnapshotData);
				
				snapshotData.Deserialize(snapshot, ref baselineData, r, ref ctx, c);
				
				serializer.As<TSerializer>().Spawn(ghostId, snapshotData);
			}

			public static bool HasComponent(ref GhostSerializerBase serializer, Entity entity)
			{
				ref var snapshotFromEntity = ref UnsafeUtilityEx.AsRef<BufferFromEntity<TSnapshotData>>(serializer.Header.SnapshotFromEntity);
				
				return snapshotFromEntity.Exists(entity);
			}
		}

		protected override void OnCreate()
		{
			base.OnCreate();

			m_Headers           = new NativeList<GhostSerializerHeader>(128, Allocator.Persistent);
			m_TypeIndexToHeader = new NativeHashMap<int, int>(128, Allocator.Persistent);
		}

		protected override JobHandle OnUpdate(JobHandle jobHandle)
		{
			return jobHandle;
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

			header.Id = m_Headers.Length;
			header.SnapshotSize              = UnsafeUtility.SizeOf<TSnapshotData>();
			header.SnapshotAlign             = UnsafeUtility.AlignOf<TSnapshotData>();
			header.Size                      = UnsafeUtility.SizeOf<TSerializer>();
			header.Align                     = UnsafeUtility.AlignOf<TSerializer>();
			header.CanSerializeFunc          = BurstCompiler.CompileFunctionPointer<d_CanSerialize>(Functions<TSerializer, TSnapshotData>.CanSerialize);
			header.BeginSerializeFunc        = new FunctionPointer<d_BeginSerialize>(Marshal.GetFunctionPointerForDelegate<d_BeginSerialize>(Functions<TSerializer, TSnapshotData>.BeginSerialize));
			header.BeginDeserializeFunc      = new FunctionPointer<d_BeginDeserialize>(Marshal.GetFunctionPointerForDelegate<d_BeginDeserialize>(Functions<TSerializer, TSnapshotData>.BeginDeserialize));
			header.SpawnFunc                 = BurstCompiler.CompileFunctionPointer<d_Spawn>(Functions<TSerializer, TSnapshotData>.Spawn);
			header.HasComponentFunc          = BurstCompiler.CompileFunctionPointer<d_HasComponent>(Functions<TSerializer, TSnapshotData>.HasComponent);
			header.SetupDeserializingFunc    = new FunctionPointer<d_SetupDeserializing>(Marshal.GetFunctionPointerForDelegate<d_SetupDeserializing>(Functions<TSerializer, TSnapshotData>.SetupDeserializing));
			header.CopyToSnapshotFunc        = BurstCompiler.CompileFunctionPointer<d_CopyToSnapshot>(Functions<TSerializer, TSnapshotData>.CopyToSnapshot);
			header.SerializeEntityFunc       = BurstCompiler.CompileFunctionPointer<d_SerializeEntity>(Functions<TSerializer, TSnapshotData>.SerializeEntity);
			header.DeserializeEntityFunc     = BurstCompiler.CompileFunctionPointer<d_DeserializeEntity>(Functions<TSerializer, TSnapshotData>.DeserializeEntity);
			header.FullDeserializeEntityFunc = BurstCompiler.CompileFunctionPointer<d_FullDeserializeEntity>(Functions<TSerializer, TSnapshotData>.FullDeserializeEntity);
			header.PredictDeltaFunc          = BurstCompiler.CompileFunctionPointer<d_PredictDelta>(Functions<TSerializer, TSnapshotData>.PredictDelta);
			header.SnapshotFromEntity        = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BufferFromEntity<TSnapshotData>>(), UnsafeUtility.AlignOf<BufferFromEntity<TSnapshotData>>(), Allocator.Persistent);

			m_Headers.Add(header);
			if (!m_TypeIndexToHeader.TryAdd(typeIndex, header.Id))
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
		
		public NativeArray<GhostSerializerReference> BeginDeserialize(JobComponentSystem system, Allocator allocator)
		{
			var referenceArray = new NativeArray<GhostSerializerReference>(m_Headers.Length, allocator);
			var systemHandle   = GCHandle.Alloc(system, GCHandleType.Pinned);
			for (var i = 0; i != m_Headers.Length; i++)
			{
				referenceArray[i] = new GhostSerializerReference(m_Headers[i], allocator);

				m_Headers[i].SetupDeserializingFunc.Invoke(ref referenceArray[i].AsRef(), systemHandle);
				m_Headers[i].BeginDeserializeFunc.Invoke(ref referenceArray[i].AsRef(), systemHandle);
			}
			systemHandle.Free();

			return referenceArray;
		}
	}
}
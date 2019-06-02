using System;
using System.Runtime.InteropServices;
using JetBrains.Annotations;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Profiling;

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

			public static unsafe void FullDeserializeEntity(ref GhostSerializerBase serializer, Entity entity, uint snapshot, uint baseline, uint baseline2, uint baseline3, void* reader, void* readerCtx,
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			                                                AtomicSafetyHandle bufferSafetyHandle,
#endif
			                                                void* compressionModel)
			{
				var r = UnsafeUtilityEx.AsRef<DataStreamReader>(reader);
				var c = UnsafeUtilityEx.AsRef<NetworkCompressionModel>(compressionModel);

				ref var header = ref serializer.Header;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
				UnsafeUtility.MemCpy(header.SnapshotFromEntity,
					UnsafeUtility.AddressOf(ref bufferSafetyHandle),
					UnsafeUtility.SizeOf<AtomicSafetyHandle>());

				UnsafeUtility.MemCpy((byte*) header.SnapshotFromEntity + UnsafeUtility.SizeOf<AtomicSafetyHandle>(),
					UnsafeUtility.AddressOf(ref bufferSafetyHandle),
					UnsafeUtility.SizeOf<AtomicSafetyHandle>());
#endif

				ref var ctx                = ref UnsafeUtilityEx.AsRef<DataStreamReader.Context>(readerCtx);
				ref var snapshotFromEntity = ref UnsafeUtilityEx.AsRef<BufferFromEntity<TSnapshotData>>(header.SnapshotFromEntity);

				var snapshotArray  = snapshotFromEntity[entity];
				var baselineData   = default(TSnapshotData);
				var snapshotLength = snapshotArray.Length;
				if (baseline != snapshot)
				{
					for (int i = 0; i < snapshotLength; ++i)
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
					for (int i = 0; i < snapshotLength; ++i)
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
				if (snapshotLength == GhostSendSystem.SnapshotHistorySize || (snapshotLength == 1 && header.WantsSingleHistory))
					snapshotArray.RemoveAt(0);
				snapshotArray.Add(data);
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

		public FunctionPointer<TDelegate> compile<TDelegate>(bool burst, TDelegate origin)
			where TDelegate : Delegate
		{
			if (burst)
			{
				return BurstCompiler.CompileFunctionPointer(origin);
			}

			return new FunctionPointer<TDelegate>(Marshal.GetFunctionPointerForDelegate(origin));
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

			header.Id            = m_Headers.Length;
			header.SnapshotSize  = UnsafeUtility.SizeOf<TSnapshotData>();
			header.SnapshotAlign = UnsafeUtility.AlignOf<TSnapshotData>();
			header.Size          = UnsafeUtility.SizeOf<TSerializer>();
			header.Align         = UnsafeUtility.AlignOf<TSerializer>();
			try
			{
				header.CanSerializeFunc          = compile<d_CanSerialize>(true, Functions<TSerializer, TSnapshotData>.CanSerialize);
				header.BeginSerializeFunc        = compile<d_BeginSerialize>(false, Functions<TSerializer, TSnapshotData>.BeginSerialize);
				header.BeginDeserializeFunc      = compile<d_BeginDeserialize>(false, Functions<TSerializer, TSnapshotData>.BeginDeserialize);
				header.SpawnFunc                 = compile<d_Spawn>(true, Functions<TSerializer, TSnapshotData>.Spawn);
				header.HasComponentFunc          = compile<d_HasComponent>(true, Functions<TSerializer, TSnapshotData>.HasComponent);
				header.SetupDeserializingFunc    = compile<d_SetupDeserializing>(false, Functions<TSerializer, TSnapshotData>.SetupDeserializing);
				header.CopyToSnapshotFunc        = compile<d_CopyToSnapshot>(true, Functions<TSerializer, TSnapshotData>.CopyToSnapshot);
				header.SerializeEntityFunc       = compile<d_SerializeEntity>(true, Functions<TSerializer, TSnapshotData>.SerializeEntity);
				header.DeserializeEntityFunc     = compile<d_DeserializeEntity>(true, Functions<TSerializer, TSnapshotData>.DeserializeEntity);
				header.FullDeserializeEntityFunc = compile<d_FullDeserializeEntity>(true, Functions<TSerializer, TSnapshotData>.FullDeserializeEntity);
				header.PredictDeltaFunc          = compile<d_PredictDelta>(true, Functions<TSerializer, TSnapshotData>.PredictDelta);
			}
			catch (Exception ex)
			{
				Debug.LogError("Couldn't burst compile. Maybe burst is not present?");
				Debug.LogException(ex);

				header.CanSerializeFunc          = compile<d_CanSerialize>(false, Functions<TSerializer, TSnapshotData>.CanSerialize);
				header.BeginSerializeFunc        = compile<d_BeginSerialize>(false, Functions<TSerializer, TSnapshotData>.BeginSerialize);
				header.BeginDeserializeFunc      = compile<d_BeginDeserialize>(false, Functions<TSerializer, TSnapshotData>.BeginDeserialize);
				header.SpawnFunc                 = compile<d_Spawn>(false, Functions<TSerializer, TSnapshotData>.Spawn);
				header.HasComponentFunc          = compile<d_HasComponent>(false, Functions<TSerializer, TSnapshotData>.HasComponent);
				header.SetupDeserializingFunc    = compile<d_SetupDeserializing>(false, Functions<TSerializer, TSnapshotData>.SetupDeserializing);
				header.CopyToSnapshotFunc        = compile<d_CopyToSnapshot>(false, Functions<TSerializer, TSnapshotData>.CopyToSnapshot);
				header.SerializeEntityFunc       = compile<d_SerializeEntity>(false, Functions<TSerializer, TSnapshotData>.SerializeEntity);
				header.DeserializeEntityFunc     = compile<d_DeserializeEntity>(false, Functions<TSerializer, TSnapshotData>.DeserializeEntity);
				header.FullDeserializeEntityFunc = compile<d_FullDeserializeEntity>(false, Functions<TSerializer, TSnapshotData>.FullDeserializeEntity);
				header.PredictDeltaFunc          = compile<d_PredictDelta>(false, Functions<TSerializer, TSnapshotData>.PredictDelta);
			}

			header.SnapshotFromEntity = UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BufferFromEntity<TSnapshotData>>(), UnsafeUtility.AlignOf<BufferFromEntity<TSnapshotData>>(), Allocator.Persistent);

			serializer.SetupHeader(this, ref header);

			Debug.Log($"Added {typeof(TSnapshotData)} Serializer for id:{header.Id}");

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
			var systemHandle   = GCHandle.Alloc(system, GCHandleType.Pinned);
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

				Profiler.BeginSample("Call func");
				m_Headers[i].SetupDeserializingFunc.Invoke(ref referenceArray[i].AsRef(), systemHandle);
				m_Headers[i].BeginDeserializeFunc.Invoke(ref referenceArray[i].AsRef(), systemHandle);
				Profiler.EndSample();
			}

			systemHandle.Free();

			return referenceArray;
		}
	}
}
using System;
using Collections.Unsafe;
using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Revolution
{
	public unsafe class DefaultSnapshotSerializer : CustomSnapshotSerializer
	{
		public struct SystemChunkData
		{
			public UnsafeList<ArchetypeChunk> Chunks;
		}

		public struct SystemGhostData
		{
			public UnsafeList<uint> GhostIds;
		}

		public UnsafeHashMap* SystemToChunks;
		public UnsafeHashMap* SystemToGhostIds;

		public DefaultSnapshotSerializer()
		{
			SystemToChunks   = UnsafeHashMap.Allocate(64, sizeof(uint), UnsafeUtility.SizeOf<SystemChunkData>());
			SystemToGhostIds = UnsafeHashMap.Allocate(64, sizeof(uint), UnsafeUtility.SizeOf<SystemGhostData>());
		}

		protected ref T GetData<T>(UnsafeHashMap<uint, IntPtr> map, uint index)
			where T : struct
		{
			return ref UnsafeUtilityEx.AsRef<T>((void*) map[index]);
		}

		public ref SystemChunkData GetChunkData(uint systemId)
		{
			if (!UnsafeHashMap.ContainsKey(SystemToChunks, systemId))
			{
				var data                     = new SystemChunkData {Chunks = new UnsafeList<ArchetypeChunk>(64, Allocator.Persistent)};
				UnsafeHashMap.Set(SystemToChunks, systemId, UnsafeUtility.AddressOf(ref data), UnsafeUtility.SizeOf<SystemChunkData>());
				
				return ref UnsafeUtilityEx.AsRef<SystemChunkData>(UnsafeHashMap.GetPtr(SystemToChunks, systemId));
			}

			return ref UnsafeUtilityEx.AsRef<SystemChunkData>(UnsafeHashMap.GetPtr(SystemToChunks, systemId));
		}

		public ref SystemGhostData GetGhostData(uint systemId)
		{
			if (!UnsafeHashMap.ContainsKey(SystemToGhostIds, systemId))
			{
				var data = new SystemGhostData {GhostIds = new UnsafeList<uint>(64, Allocator.Persistent)};
				UnsafeHashMap.Set(SystemToGhostIds, systemId, UnsafeUtility.AddressOf(ref data), UnsafeUtility.SizeOf<SystemGhostData>());

				return ref UnsafeUtilityEx.AsRef<SystemGhostData>(UnsafeHashMap.GetPtr(SystemToGhostIds, systemId));
			}

			return ref UnsafeUtilityEx.AsRef<SystemGhostData>(UnsafeHashMap.GetPtr(SystemToGhostIds, systemId));
		}

		public override void ClearChunks(uint systemId, IDynamicSnapshotSystem system)
		{
			ref var chunks = ref GetChunkData(systemId);
			chunks.Chunks.Clear();
		}

		public override void AddChunk(uint systemId, IDynamicSnapshotSystem system, ArchetypeChunk chunk)
		{
			ref var chunks = ref GetChunkData(systemId);
			chunks.Chunks.Add(chunk);
		}

		public override void Serialize(SerializeClientData jobData, NativeList<SortDelegate<OnSerializeSnapshot>> delegateSerializers, DataStreamWriter writer, NativeList<byte> outgoing, bool debugRange)
		{
			new SerializeJob
			{
				SystemChunkMap = SystemToChunks,
				
				ClientData   = jobData,
				Serializers  = delegateSerializers,
				StreamWriter = writer,
				OutgoingData = outgoing,

				DebugRange = debugRange
			}.Run();
		}

		public override void ClearGhosts(uint systemId)
		{
			ref var ghosts = ref GetGhostData(systemId);
			ghosts.GhostIds.Clear();
		}

		public override void AddGhost(uint systemId, uint ghostId)
		{
			ref var ghosts = ref GetGhostData(systemId);
			ghosts.GhostIds.Add(ghostId);
		}

		public override void RemoveGhost(uint systemId, uint ghostId)
		{
			ref var ghosts = ref GetGhostData(systemId);
			ref var ghostArray = ref ghosts.GhostIds;
			for (var i = 0; i < ghostArray.Length; i++)
			{
				if (ghostArray.Ptr[i] != ghostId)
					continue;
				var ptr = ghostArray.Ptr;

				ghostArray.Length--;
				UnsafeUtility.MemMove((byte*) ptr + i * sizeof(uint), (byte*) ptr + (1 + i) * sizeof(uint), sizeof(uint) * (ghostArray.Length - i));

				//ghostArray.RemoveAtSwapBack(i);
			}
		}

		public override void Deserialize(DeserializeClientData jobData, NativeList<SortDelegate<OnDeserializeSnapshot>> delegateDeserializers, NativeArray<byte> data, NativeArray<DataStreamReader.Context> readCtxArray, bool debugRange)
		{
			new DeserializeJob
			{
				ClientData    = jobData,
				Deserializers = delegateDeserializers,
				StreamData    = data,
				ReadContext   = readCtxArray,

				SystemGhostIdMap = SystemToGhostIds,

				DebugRange = debugRange
			}.Run();
		}

		public override void Dispose()
		{
			foreach (var kvp in UnsafeHashMap.GetIterator<uint, IntPtr>(SystemToChunks))
			{
				UnsafeUtilityEx.AsRef<SystemChunkData>(UnsafeHashMap.GetPtr(SystemToChunks, kvp.key)).Chunks.Dispose();
			}

			foreach (var kvp in UnsafeHashMap.GetIterator<uint, IntPtr>(SystemToGhostIds))
			{
				UnsafeUtilityEx.AsRef<SystemGhostData>(UnsafeHashMap.GetPtr(SystemToGhostIds, kvp.key)).GhostIds.Dispose();
			}

			UnsafeHashMap.Free(SystemToChunks);
			UnsafeHashMap.Free(SystemToGhostIds);
		}

		[BurstCompile]
		public unsafe struct SerializeJob : IJob
		{
			public bool DebugRange;

			[NativeDisableUnsafePtrRestriction]
			public UnsafeHashMap* SystemChunkMap;

			public NativeList<SortDelegate<OnSerializeSnapshot>> Serializers;
			public SerializeClientData                           ClientData;

			public DataStreamWriter StreamWriter;
			public NativeList<byte> OutgoingData;

			public void Execute()
			{
				var parameters = new SerializeParameters
				{
					m_ClientData = new Blittable<SerializeClientData>(ref ClientData),
					m_Stream     = new Blittable<DataStreamWriter>(ref StreamWriter)
				};
				if (DebugRange)
				{
					for (int i = 0, serializerLength = Serializers.Length; i != serializerLength; i++)
					{
						var serializer = Serializers[i];
						var invoke     = serializer.Value.Invoke;

						StreamWriter.Write(StreamWriter.Length);
						var expected = StreamWriter.Write(0);

						parameters.SystemId = serializer.SystemId;

						var chunks = UnsafeUtilityEx.AsRef<SystemChunkData>(UnsafeHashMap.GetPtr(SystemChunkMap, serializer.SystemId)).Chunks;
						parameters.ChunksToSerialize = new UnsafeAllocationLength<ArchetypeChunk>(chunks.Ptr, chunks.Length);
						invoke(ref parameters);

						StreamWriter.Flush();
						expected.Update(StreamWriter.Length);
					}
				}
				else
				{
					for (int i = 0, serializerLength = Serializers.Length; i != serializerLength; i++)
					{
						var serializer = Serializers[i];
						var invoke     = serializer.Value.Invoke;

						parameters.SystemId = serializer.SystemId;

						var chunks = UnsafeUtilityEx.AsRef<SystemChunkData>(UnsafeHashMap.GetPtr(SystemChunkMap, serializer.SystemId)).Chunks;
						parameters.ChunksToSerialize = new UnsafeAllocationLength<ArchetypeChunk>(chunks.Ptr, chunks.Length);
						invoke(ref parameters);
					}
				}

				StreamWriter.Write(0);

				OutgoingData.AddRange(StreamWriter.GetUnsafePtr(), StreamWriter.Length);
			}
		}

		[BurstCompile]
		public struct DeserializeJob : IJob
		{
			[NativeDisableUnsafePtrRestriction]
			public UnsafeHashMap* SystemGhostIdMap;

			public NativeList<SortDelegate<OnDeserializeSnapshot>> Deserializers;
			public DeserializeClientData                           ClientData;

			public NativeArray<byte>                     StreamData;
			public NativeArray<DataStreamReader.Context> ReadContext;

			public bool DebugRange;

			[BurstDiscard]
			private void ThrowError(int currLength, int byteRead, int i, SortDelegate<OnDeserializeSnapshot> serializer)
			{
				Debug.LogError($"Invalid Length [{currLength} != {byteRead}] at index {i}, system {serializer.Name.ToString()}, previous system {Deserializers[math.max(i - 1, 0)].Name.ToString()}");
			}

			[BurstDiscard]
			private void ThrowErrorExpected(int expected, int byteRead, int i, SortDelegate<OnDeserializeSnapshot> serializer)
			{
				Debug.LogError($"Expected Length={expected} but we are at {byteRead} at index {i}, system {serializer.Name.ToString()}.");
			}

			public void Execute()
			{
				var reader = new DataStreamReader(StreamData);
				var parameters = new DeserializeParameters
				{
					m_ClientData = new Blittable<DeserializeClientData>(ref ClientData),
					Stream       = reader,
					Ctx          = ReadContext[0]
				};

				if (DebugRange)
				{
					for (var i = 0; i < Deserializers.Length; i++)
					{
						var serializer = Deserializers[i];
						var invoke     = serializer.Value.Invoke;

						var byteRead   = reader.GetBytesRead(ref parameters.Ctx);
						var currLength = reader.ReadInt(ref parameters.Ctx);
						if (currLength != byteRead)
						{
							ThrowError(currLength, byteRead, i, serializer);
							return;
						}

						var expected = reader.ReadInt(ref parameters.Ctx);

						parameters.SystemId = serializer.SystemId;

						var ghostIds = UnsafeUtilityEx.AsRef<SystemGhostData>(UnsafeHashMap.GetPtr(SystemGhostIdMap, serializer.SystemId)).GhostIds;
						parameters.GhostsToDeserialize = new UnsafeAllocationLength<uint>(ghostIds.Ptr, ghostIds.Length);
						invoke(ref parameters);

						byteRead = reader.GetBytesRead(ref parameters.Ctx);
						if (byteRead != expected)
						{
							ThrowErrorExpected(expected, byteRead, i, serializer);
							return;
						}
					}
				}
				else
				{
					for (int i = 0, deserializeLength = Deserializers.Length; i < deserializeLength; i++)
					{
						var serializer = Deserializers[i];
						var invoke     = serializer.Value.Invoke;

						parameters.SystemId = serializer.SystemId;

						var ghostIds = UnsafeUtilityEx.AsRef<SystemGhostData>(UnsafeHashMap.GetPtr(SystemGhostIdMap, serializer.SystemId)).GhostIds;
						parameters.GhostsToDeserialize = new UnsafeAllocationLength<uint>(ghostIds.Ptr, ghostIds.Length);
						invoke(ref parameters);
					}
				}

				ReadContext[0] = parameters.Ctx;
			}
		}
	}
}
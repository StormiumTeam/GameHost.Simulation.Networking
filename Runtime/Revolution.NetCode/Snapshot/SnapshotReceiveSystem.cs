using K4os.Compression.LZ4;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution.NetCode
{
	[AlwaysUpdateSystem]
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateBefore(typeof(NetworkReceiveSnapshotSystemGroup))]
	public unsafe class SnapshotReceiveSystem : ComponentSystem
	{
		private EntityQuery           m_PlayerQuery;
		private DeserializeClientData m_DeserializeData;

		private ApplySnapshotSystem m_ApplySnapshotSystem;

		public DeserializeClientData JobData => m_DeserializeData;

		private uint m_PreviousTick;

		protected override void OnCreate()
		{
			m_DeserializeData = new DeserializeClientData(Allocator.Persistent);
			m_PlayerQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkStreamInGame)},
				None = new ComponentType[] {typeof(NetworkStreamDisconnected)}
			});

			m_ApplySnapshotSystem = World.GetOrCreateSystem<ApplySnapshotSystem>();

			m_PreviousTick = uint.MaxValue;
		}

		protected override void OnUpdate()
		{
			if (m_PlayerQuery.IsEmptyIgnoreFilter)
			{
				m_DeserializeData.Entities.Clear();
				m_DeserializeData.GhostIds.Clear();
				m_DeserializeData.KnownArchetypes.Clear();
				m_DeserializeData.GhostToEntityMap.Clear();
				return;
			}

			var player = m_PlayerQuery.GetSingletonEntity();
			var incomingData = EntityManager.GetBuffer<IncomingSnapshotStreamBufferComponent>(player).Reinterpret<byte>();
			if (incomingData.Length == 0)
				return;

			var snapshot = incomingData.ToNativeArray(Allocator.TempJob);
			var reader = new DataStreamReader(snapshot);
			var ctx    = default(DataStreamReader.Context);
			while (reader.GetBytesRead(ref ctx) < reader.Length)
			{
				var tick = reader.ReadUInt(ref ctx);
				if (m_PreviousTick != tick - 1 && m_PreviousTick != uint.MaxValue)
				{
					Debug.LogError($"Reliability issue {m_PreviousTick} {tick - 1}");
					Application.Quit();
				}

				m_PreviousTick = tick;
				m_DeserializeData.Tick = tick;

				var snapshotAck = EntityManager.GetComponentData<NetworkSnapshotAckComponent>(player);
				{
					var compressedSize   = reader.ReadInt(ref ctx);
					var uncompressedSize = reader.ReadInt(ref ctx);
					var compressedMemory = new NativeArray<byte>(compressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
					reader.ReadBytes(ref ctx, (byte*) compressedMemory.GetUnsafePtr(), compressedSize);
						
						
					var uncompressedMemory = new NativeArray<byte>(uncompressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory); 
						
					LZ4Codec.Decode((byte*) compressedMemory.GetUnsafePtr(), compressedSize, 
						(byte*) uncompressedMemory.GetUnsafePtr(), uncompressedSize);
						
					m_ApplySnapshotSystem.ApplySnapshot(ref m_DeserializeData, uncompressedMemory);
						
					uncompressedMemory.Dispose();
					compressedMemory.Dispose();

					snapshotAck.LastReceivedSnapshotByLocal = tick;
				}
				EntityManager.SetComponentData(player, snapshotAck);
			}
			
			EntityManager.GetBuffer<IncomingSnapshotStreamBufferComponent>(player).Clear();
			snapshot.Dispose();
		}

		protected override void OnDestroy()
		{
			m_DeserializeData.Dispose();
		}
	}
}
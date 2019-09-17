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
	public unsafe class SnapshotReceiveSystem : ComponentSystem
	{
		private EntityQuery           m_PacketQuery;
		private EntityQuery           m_PlayerQuery;
		private DeserializeClientData m_DeserializeData;

		private ApplySnapshotSystem m_ApplySnapshotSystem;

		public DeserializeClientData JobData => m_DeserializeData;

		protected override void OnCreate()
		{
			m_DeserializeData = new DeserializeClientData(Allocator.Persistent);
			m_PacketQuery = GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {typeof(IncomingPacket), typeof(SnapshotPacketTag), typeof(IncomingData)}
			});
			m_PlayerQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkStreamInGame)},
				None = new ComponentType[] {typeof(NetworkStreamDisconnected)}
			});

			m_ApplySnapshotSystem = World.GetOrCreateSystem<ApplySnapshotSystem>();
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

			// Consume packets...
			using (var packetEntities = m_PacketQuery.ToEntityArray(Allocator.TempJob))
			{
				foreach (var packet in packetEntities)
				{
					var snapshotBuffer = EntityManager.GetBuffer<IncomingData>(packet);
					if (snapshotBuffer.Length <= 0)
						return;

					var snapshotData = snapshotBuffer.Reinterpret<byte>().ToNativeArray(Allocator.TempJob);
					snapshotBuffer.Clear();

					var reader = new DataStreamReader(snapshotData);
					var ctx    = default(DataStreamReader.Context);

					var tick = reader.ReadUInt(ref ctx);
					m_DeserializeData.Tick = tick;

					var snapshotAck = EntityManager.GetComponentData<NetworkSnapshotAckComponent>(player);
					{
						var compressedSize = reader.ReadInt(ref ctx);
						var uncompressedSize = reader.ReadInt(ref ctx);
						var compressedMemory = new NativeArray<byte>(compressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
						reader.ReadBytes(ref ctx, (byte*) compressedMemory.GetUnsafePtr(), compressedSize);
						
						
						var uncompressedMemory = new NativeArray<byte>(uncompressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory); 
						
						LZ4Codec.Decode((byte*) compressedMemory.GetUnsafePtr(), compressedSize, 
							(byte*) uncompressedMemory.GetUnsafePtr(), uncompressedSize);
						
						Debug.Log($"compressed size = {compressedSize} {reader.ReadInt(ref ctx)}, uncompressed = {uncompressedSize}");
						
						m_ApplySnapshotSystem.ApplySnapshot(ref m_DeserializeData, uncompressedMemory);
						
						uncompressedMemory.Dispose();
					}
					EntityManager.SetComponentData(player, snapshotAck);
				}

				EntityManager.DestroyEntity(m_PacketQuery);
			}
		}

		protected override void OnDestroy()
		{
			m_DeserializeData.Dispose();
		}
	}
}
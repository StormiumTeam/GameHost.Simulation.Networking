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
		private struct DelayedSnapshotInfo : IComponentData
		{
			public uint tick;
		}
		
		private struct DelayedSnapshotBuffer : IBufferElementData
		{
			public byte val;
		}
		
		private EntityQuery           m_PlayerQuery;
		private EntityQuery m_Delayed;
		
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
			m_Delayed = GetEntityQuery(typeof(DelayedSnapshotInfo), typeof(DelayedSnapshotBuffer));

			m_ApplySnapshotSystem = World.GetOrCreateSystem<ApplySnapshotSystem>();

			m_PreviousTick = uint.MaxValue;
		}

		private void ApplySnapshot(uint tick, NativeArray<byte> data)
		{
			m_DeserializeData.Tick = tick;
			m_ApplySnapshotSystem.ApplySnapshot(ref m_DeserializeData, data);
			m_PreviousTick = tick;
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
			{
				return;
			}

			var snapshot = incomingData.ToNativeArray(Allocator.TempJob);
			var reader = new DataStreamReader(snapshot);
			var ctx    = default(DataStreamReader.Context);
			while (reader.GetBytesRead(ref ctx) < reader.Length)
			{
				var safetyData = reader.ReadByte(ref ctx);
				var tick = reader.ReadUInt(ref ctx);
				var needDelay = false;
				if (tick != m_PreviousTick + 1 && m_PreviousTick != uint.MaxValue)
				{
					var isReliable = false;
					using (var snapshotEntities = m_Delayed.ToEntityArray(Allocator.TempJob))
					using (var snapshotInfoArray = m_Delayed.ToComponentDataArray<DelayedSnapshotInfo>(Allocator.TempJob))
					{
						var min = uint.MaxValue;
						var max = 0u;
						// get min-max first
						foreach (var info in snapshotInfoArray)
						{
							if (info.tick < min)
								min = info.tick;
							if (info.tick > max)
								max = info.tick;
						}

						// we can only continue if the incoming snapshot is right after the previous one
						if (max + 1 == tick)
						{
							isReliable = true;

							var targets = new NativeList<Entity>(snapshotEntities.Length, Allocator.Temp);
							var p       = min;
							for (var index = 0; index < snapshotInfoArray.Length;)
							{
								var info = snapshotInfoArray[index];
								if (p != info.tick)
								{
									index++;
									continue;
								}

								targets.Add(snapshotEntities[index]);
								p++;

								// reset index
								index = 0;
							}

							if (targets.Length != snapshotEntities.Length)
							{
								Debug.LogWarning("Did not get enough snapshots...");
								isReliable = false;
							}

							if (isReliable)
							{
								for (var index = 0; index < targets.Length; index++)
								{
									using (var data = EntityManager.GetBuffer<DelayedSnapshotBuffer>(snapshotEntities[index])
									                               .Reinterpret<byte>()
									                               .ToNativeArray(Allocator.TempJob))
									{
										var info = snapshotInfoArray[index];
										ApplySnapshot(info.tick, data);
									}
								}
								
								EntityManager.DestroyEntity(m_Delayed);
							}
						}
					}

					if (!isReliable)
					{
						Debug.LogError($"Reliability issue, s={safetyData} p={m_PreviousTick} n={tick}");
						needDelay = true;
					}
				}

				var snapshotAck = EntityManager.GetComponentData<NetworkSnapshotAckComponent>(player);
				{
					var compressedSize   = reader.ReadInt(ref ctx);
					var uncompressedSize = reader.ReadInt(ref ctx);
					var compressedMemory = new NativeArray<byte>(compressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
					reader.ReadBytes(ref ctx, (byte*) compressedMemory.GetUnsafePtr(), compressedSize);

					var uncompressedMemory = new NativeArray<byte>(uncompressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory); 
						
					LZ4Codec.Decode((byte*) compressedMemory.GetUnsafePtr(), compressedSize, 
						(byte*) uncompressedMemory.GetUnsafePtr(), uncompressedSize);

					if (!needDelay)
					{
						ApplySnapshot(tick, uncompressedMemory);
					}
					else
					{
						var delayed = EntityManager.CreateEntity(typeof(DelayedSnapshotInfo), typeof(DelayedSnapshotBuffer));
						EntityManager.GetBuffer<DelayedSnapshotBuffer>(delayed)
						             .Reinterpret<byte>()
						             .AddRange(uncompressedMemory);
						EntityManager.SetComponentData(delayed, new DelayedSnapshotInfo {tick = tick});
					}

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
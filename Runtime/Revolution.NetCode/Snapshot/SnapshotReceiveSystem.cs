using System;
using K4os.Compression.LZ4;
using Revolution;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.NetCode
{
	[AlwaysUpdateSystem]
	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	[UpdateBefore(typeof(NetworkReceiveSnapshotSystemGroup))]
	[UpdateBefore(typeof(GhostSimulationSystemGroup))]
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

		public int LastCompressedSnapshotSize;
		public int LastUncompressedSnapshotSize;
		
		private EntityQuery           m_PlayerQuery;
		private EntityQuery m_Delayed;
		private EntityQuery m_ReplicatedQuery;
		
		private DeserializeClientData m_DeserializeData;

		private ApplySnapshotSystem m_ApplySnapshotSystem;

		public DeserializeClientData JobData => m_DeserializeData;

		private uint m_PreviousTick;
		private JobHandle m_Dependency;
		
		protected override void OnCreate()
		{
			m_DeserializeData = new DeserializeClientData(Allocator.Persistent);
			m_PlayerQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkStreamInGame)},
				None = new ComponentType[] {typeof(NetworkStreamDisconnected)}
			});
			m_Delayed = GetEntityQuery(typeof(DelayedSnapshotInfo), typeof(DelayedSnapshotBuffer));
			m_ReplicatedQuery = GetEntityQuery(typeof(ReplicatedEntity), ComponentType.Exclude<ManualDestroy>());

			m_ApplySnapshotSystem = World.GetOrCreateSystem<ApplySnapshotSystem>();

			m_PreviousTick = uint.MaxValue;
		}

		private int m_ErrorCount;
		private void ApplySnapshot(uint tick, NativeArray<byte> data)
		{
			var player      = m_PlayerQuery.GetSingletonEntity();
			var snapshotAck = EntityManager.GetComponentData<NetworkSnapshotAckComponent>(player);
			snapshotAck.LastReceivedSnapshotByLocal = tick;
			EntityManager.SetComponentData(player, snapshotAck);
			
			m_DeserializeData.Tick = tick;
			try
			{
				m_ApplySnapshotSystem.ApplySnapshot(ref m_DeserializeData, data);
			}
			catch (Exception ex)
			{
				Debug.LogError(ex);
				
				m_ErrorCount++;
				if (m_ErrorCount > 4)
				{
					Debug.LogError("Quit application before of snapshots errors...");
					Application.Quit();
					return;
				}
			}

			m_PreviousTick = tick;
		}

		public JobHandle AddDependency(JobHandle inputDeps)
		{
			m_Dependency = JobHandle.CombineDependencies(m_Dependency, inputDeps);
			return inputDeps;
		}

		protected override void OnUpdate()
		{
			if (m_PlayerQuery.IsEmptyIgnoreFilter)
			{
				m_DeserializeData.Entities.Clear();
				m_DeserializeData.GhostIds.Clear();
				m_DeserializeData.KnownArchetypes.Clear();
				m_DeserializeData.GhostToEntityMap.Clear();

				using (var replicatedEntities = m_ReplicatedQuery.ToEntityArray(Allocator.TempJob))
				{
					foreach (var ent in replicatedEntities)
						EntityManager.DestroyEntity(ent);
				}

				return;
			}

			var player = m_PlayerQuery.GetSingletonEntity();
			var incomingData = EntityManager.GetBuffer<IncomingSnapshotDataStreamBufferComponent>(player).Reinterpret<byte>();
			if (incomingData.Length == 0)
			{
				return;
			}
			
			m_Dependency.Complete();
			m_Dependency = default;

			var snapshot = incomingData.ToNativeArray(Allocator.TempJob);
			var reader = new DataStreamReader(snapshot);
			var ctx    = default(DataStreamReader.Context);
			var snapshotCount = 0;
			while (reader.GetBytesRead(ref ctx) < reader.Length)
			{
				var safetyData = reader.ReadByte(ref ctx);
				var tick = reader.ReadUInt(ref ctx);
				var needDelay = false;
				
				Profiler.BeginSample("Apply Delayed");
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
									using (var data = EntityManager.GetBuffer<DelayedSnapshotBuffer>(targets[index])
									                               .Reinterpret<byte>()
									                               .ToNativeArray(Allocator.TempJob))
									{
										var info = EntityManager.GetComponentData<DelayedSnapshotInfo>(targets[index]);
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
				Profiler.EndSample();

				Profiler.BeginSample("Apply current snapshot");
				{
					var compressedSize   = reader.ReadInt(ref ctx);
					var uncompressedSize = reader.ReadInt(ref ctx);
					var compressedMemory = new NativeArray<byte>(compressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
					reader.ReadBytes(ref ctx, (byte*) compressedMemory.GetUnsafePtr(), compressedSize);

					var uncompressedMemory = new NativeArray<byte>(uncompressedSize, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

					LastCompressedSnapshotSize = compressedSize;
					LastUncompressedSnapshotSize = uncompressedSize;
					
					Profiler.BeginSample("Decompress");
					LZ4Codec.Decode((byte*) compressedMemory.GetUnsafePtr(), compressedSize, 
						(byte*) uncompressedMemory.GetUnsafePtr(), uncompressedSize);
					Profiler.EndSample();

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
						Debug.Log("Add delayed snapshot to the table...");
					}

					uncompressedMemory.Dispose();
					compressedMemory.Dispose();

					snapshotCount++;
				}
				Profiler.EndSample();
			}

			EntityManager.GetBuffer<IncomingSnapshotDataStreamBufferComponent>(player).Clear();
			snapshot.Dispose();
		}

		protected override void OnDestroy()
		{
			m_DeserializeData.Dispose();
		}
	}
}
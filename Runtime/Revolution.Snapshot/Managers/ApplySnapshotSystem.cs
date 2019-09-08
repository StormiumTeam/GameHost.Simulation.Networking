using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Revolution
{
	public class AfterSnapshotIsAppliedSystemGroup : ComponentSystemGroup
	{
	}

	public class ApplySnapshotSystem : JobComponentSystem
	{
		private SnapshotManager                    m_SnapshotManager;
		private Dictionary<uint, NativeList<uint>> m_SystemToGhostIds;

		protected override void OnCreateManager()
		{
			base.OnCreateManager();

			m_SystemToGhostIds = new Dictionary<uint, NativeList<uint>>();
		}

		[BurstCompile(CompileSynchronously = true)]
		public unsafe struct DeserializeJob : IJob
		{
			public NativeList<SortDelegate<OnDeserializeSnapshot>> Deserializers;
			public DeserializeClientData                           ClientData;

			public NativeArray<byte>                     StreamData;
			public NativeArray<DataStreamReader.Context> ReadContext;

			public void Execute()
			{
				var reader  = new DataStreamReader(StreamData);
				var readCtx = ReadContext[0];

				Deserializers.Sort();
				for (var i = 0; i < Deserializers.Length; i++)
				{
					var serializer = Deserializers[i];
					var invoke     = serializer.Value.Invoke;

					invoke((uint) i, ClientData.Tick, ref ClientData, ref reader, ref readCtx);
				}

				ReadContext[0] = readCtx;
			}
		}

		protected override void OnCreate()
		{
			base.OnCreate();

			m_SnapshotManager = World.GetOrCreateSystem<SnapshotManager>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return inputDeps;
		}

		private void ReadArchetypes(in DeserializeClientData baseline, in DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var newArchetypeLength = reader.ReadInt(ref ctx);
			{
				var previousArchetypeId = 0u;
				for (var i = 0; i < newArchetypeLength; i++)
				{
					var id          = reader.ReadPackedUIntDelta(ref ctx, previousArchetypeId, baseline.NetworkCompressionModel);
					var systemCount = reader.ReadPackedUInt(ref ctx, baseline.NetworkCompressionModel);
					var systemArray = new NativeArray<uint>((int) systemCount, Allocator.Persistent);
					for (var comp = 0; comp < systemCount; comp++)
					{
						// TEMPORARY METHOD, IN FUTURE, THE ARCHETYPE SHOULD BE STORED IN 'DeserializeClientData'
						systemArray[comp] = reader.ReadPackedUInt(ref ctx, baseline.NetworkCompressionModel);
					}

					m_SnapshotManager.ForceSetArchetype(id, systemArray);

					previousArchetypeId = id;
				}
			}
		}

		public unsafe void ApplySnapshot(ref DeserializeClientData baseline, NativeArray<byte> data, ref DataStreamReader.Context ctx)
		{
			var reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) data.GetUnsafePtr(), data.Length);

			baseline.Tick = reader.ReadUInt(ref ctx);

			if (reader.ReadByte(ref ctx) == 60)
			{
				if (reader.ReadUInt(ref ctx) != baseline.Tick)
					throw new InvalidOperationException("Invalid header");
			}
			else
				throw new InvalidOperationException("Invalid header");

			// Be sure that all systems are ready...
			foreach (var system in m_SnapshotManager.IdToSystems)
			{
				if (system.Value is IDynamicSnapshotSystem dynamicSystem)
				{
					ref var sharedGhost = ref dynamicSystem.GetSharedGhost();
					if (!m_SystemToGhostIds.TryGetValue(system.Key, out var list))
					{
						list                           = new NativeList<uint>(64, Allocator.Persistent);
						m_SystemToGhostIds[system.Key] = list;
					}

					sharedGhost.Ghosts = list;
				}
			}

			var entityLength    = reader.ReadInt(ref ctx);
			var ghostUpdate     = new NativeList<uint>(entityLength, Allocator.TempJob);
			var entityUpdate    = new NativeList<Entity>(entityLength, Allocator.TempJob);
			var archetypeUpdate = new NativeList<uint>(entityLength, Allocator.TempJob);

			if (entityLength > 0)
			{
				ReadArchetypes(in baseline, in reader, ref ctx);
				reader.ReadByte(ref ctx); // DON'T REMOVE THIS LINE

				var newGhostArray  = new NativeArray<uint>(entityLength, Allocator.Temp);
				var newEntityArray = new NativeArray<Entity>(entityLength, Allocator.Temp);

				var previousGhostId   = 0u;
				var previousArchetype = 0u;
				for (var ent = 0; ent < entityLength; ent++)
				{
					var ghostId   = reader.ReadPackedUIntDelta(ref ctx, previousGhostId, baseline.NetworkCompressionModel);
					var archetype = reader.ReadPackedUIntDelta(ref ctx, previousArchetype, baseline.NetworkCompressionModel);

					previousGhostId   = ghostId;
					previousArchetype = archetype;

					var exists     = false;
					var ghostIndex = 0;
					for (; ghostIndex < baseline.GhostIds.Length; ghostIndex++)
					{
						if (ghostId == baseline.GhostIds[ghostIndex])
						{
							exists = true;
							break;
						}
					}

					Entity targetWorldEntity = default;
					if (exists)
						targetWorldEntity = baseline.Entities[ghostIndex];

					var isNew = false;
					if (targetWorldEntity == default || !EntityManager.Exists(targetWorldEntity))
					{
						targetWorldEntity = EntityManager.CreateEntity(typeof(ReplicatedEntity));
						EntityManager.SetComponentData(targetWorldEntity, new ReplicatedEntity {GhostId = ghostId, Archetype = archetype});

						isNew = true;
					}

					// The archetype got changed (or it's a new entity)
					if (EntityManager.GetComponentData<ReplicatedEntity>(targetWorldEntity).Archetype != archetype || isNew)
					{
						ghostUpdate.Add(ghostId);
						entityUpdate.Add(targetWorldEntity);
						archetypeUpdate.Add(archetype);
					}

					newGhostArray[ent]  = ghostId;
					newEntityArray[ent] = targetWorldEntity;

					if (baseline.GhostToEntityMap.ContainsKey(ghostId))
						baseline.GhostToEntityMap.Remove(ghostId);
					baseline.GhostToEntityMap.TryAdd(ghostId, targetWorldEntity);
				}

				// Now check for removed entities...
				var baselineGhostArray = baseline.GhostIds;
				for (var ent = 0; ent < baselineGhostArray.Length; ent++)
				{
					var ghostId = baselineGhostArray[ent];
					if (newGhostArray.Contains(ghostId))
						continue;

					foreach (var system in m_SystemToGhostIds)
					{
						if (m_SnapshotManager.GetSystem(system.Key) is IDynamicSnapshotSystem dynamicSystem)
						{
							var systemGhosts = dynamicSystem.GetSharedGhost().Ghosts;
							for (var i = 0; i < systemGhosts.Length; i++)
							{
								if (systemGhosts[i] != ghostId)
									continue;
								systemGhosts.RemoveAtSwapBack(i);
							}
						}
					}

					if (baseline.GhostToEntityMap.ContainsKey(ghostId))
						baseline.GhostToEntityMap.Remove(ghostId);

					var baselineEnt = baseline.Entities[ent];
					if (EntityManager.Exists(baselineEnt))
					{
						if (!EntityManager.HasComponent(baselineEnt, typeof(ManualDestroy)))
							EntityManager.DestroyEntity(baselineEnt);
						else
							EntityManager.AddComponent(baselineEnt, typeof(IsDestroyedOnSnapshot));
					}
				}

				baseline.Entities.Clear();
				baseline.GhostIds.Clear();

				baseline.Entities.AddRange(newEntityArray);
				baseline.GhostIds.AddRange(newGhostArray);
			}
			else
			{
				var entityChangeCount = reader.ReadInt(ref ctx);
				if (entityChangeCount >= 0)
				{
					ReadArchetypes(in baseline, in reader, ref ctx);
					reader.ReadByte(ref ctx); // DON'T REMOVE THIS LINE

					var previousGhostId   = 0u;
					var previousArchetype = 0u;
					for (var i = 0; i < entityChangeCount; i++)
					{
						// we use the index instead of ID since the list is in the same order as the sender.
						var ghostIndex = reader.ReadPackedUIntDelta(ref ctx, previousGhostId, baseline.NetworkCompressionModel);
						var archetype  = reader.ReadPackedUIntDelta(ref ctx, previousArchetype, baseline.NetworkCompressionModel);

						previousGhostId   = ghostIndex;
						previousArchetype = archetype;

						ghostUpdate.Add(baseline.GhostIds[(int) ghostIndex]);
						entityUpdate.Add(baseline.Entities[(int) ghostIndex]);
						archetypeUpdate.Add(archetype);
					}

					reader.ReadByte(ref ctx); // DON'T REMOVE THIS LINE
				}
			}

			if (entityUpdate.Length > 0)
			{
				foreach (var kvp in m_SystemToGhostIds)
				{
					var ghostList = kvp.Value;
					ghostList.Clear();
				}

				for (var ent = 0; ent < entityUpdate.Length; ent++)
				{
					var entity    = entityUpdate[ent];
					var archetype = archetypeUpdate[ent];

					EntityManager.SetComponentData(entity, new ReplicatedEntity {GhostId = ghostUpdate[ent], Archetype = archetype});
				}

				for (var index = 0; index < baseline.Entities.Length; index++)
				{
					var entity  = baseline.Entities[index];
					var repl    = EntityManager.GetComponentData<ReplicatedEntity>(entity);
					var systems = m_SnapshotManager.ArchetypeToSystems[repl.Archetype];

					foreach (var sys in systems)
					{
						m_SystemToGhostIds[sys].Add(baseline.GhostIds[index]);
					}
				}

				foreach (var system in m_SnapshotManager.IdToSystems.Values)
				{
					if (system is IDynamicSnapshotSystem dynamicSystem)
					{
						dynamicSystem.OnDeserializerArchetypeUpdate(entityUpdate, archetypeUpdate, m_SnapshotManager.ArchetypeToSystems);
					}
				}
			}

			ghostUpdate.Dispose();
			entityUpdate.Dispose();
			archetypeUpdate.Dispose();

			var delegateDeserializers = new NativeList<SortDelegate<OnDeserializeSnapshot>>(m_SnapshotManager.IdToSystems.Count, Allocator.TempJob);
			var delegateGroup         = World.GetExistingSystem<SnapshotWithDelegateSystemGroup>();

			delegateGroup.BeginDeserialize(baseline.Client, ref delegateDeserializers);

			var readCtxArray = new NativeArray<DataStreamReader.Context>(1, Allocator.TempJob)
			{
				[0] = ctx
			};
			new DeserializeJob
			{
				Deserializers = delegateDeserializers,
				ClientData    = baseline,
				StreamData    = data,
				ReadContext   = readCtxArray
			}.Run();

			ctx = readCtxArray[0];
			readCtxArray.Dispose();

			World.GetOrCreateSystem<AfterSnapshotIsAppliedSystemGroup>().Update();
		}
	}
}
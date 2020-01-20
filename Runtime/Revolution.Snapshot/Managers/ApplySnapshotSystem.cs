using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Revolution
{
	public class AfterSnapshotIsAppliedSystemGroup : ComponentSystemGroup
	{
		protected override void OnUpdate()
		{
		}

		public void ForceUpdate()
		{
			base.OnUpdate();
		}
	}

	public class ApplySnapshotSystem : JobComponentSystem
	{
		private SnapshotManager m_SnapshotManager;
		private HashSet<uint>   m_DynamicSystemIds;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_SnapshotManager  = World.GetOrCreateSystem<SnapshotManager>();
			m_DynamicSystemIds = new HashSet<uint>();
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
						// TEMPORARY METHOD, IN FUTURE, THE ARCHETYPE SHOULD BE STORED IN 'DeserializeClientData'
						systemArray[comp] = reader.ReadPackedUInt(ref ctx, baseline.NetworkCompressionModel);

					m_SnapshotManager.ForceSetArchetype(id, systemArray);

					previousArchetypeId = id;
				}
			}
			reader.ReadByte(ref ctx);
		}

		public unsafe void ApplySnapshot(ref DeserializeClientData baseline, NativeArray<byte> data)
		{
			var reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) data.GetUnsafePtr(), data.Length);
			var ctx    = default(DataStreamReader.Context);

			// Be sure that all systems are ready...
			Profiler.BeginSample("System tidying");
			foreach (var system in m_SnapshotManager.IdToSystems)
				if (system.Value is IDynamicSnapshotSystem dynamicSystem)
				{
					m_DynamicSystemIds.Add(system.Key);
				}

			Profiler.EndSample();

			var debugRange       = reader.ReadByte(ref ctx) == 1;
			var entityLength     = reader.ReadInt(ref ctx);
			var ghostUpdate      = new NativeList<uint>(entityLength, Allocator.TempJob);
			var ghostIndexUpdate = new NativeList<uint>(entityLength, Allocator.TempJob);
			var entityUpdate     = new NativeList<Entity>(entityLength, Allocator.TempJob);
			var archetypeUpdate  = new NativeList<uint>(entityLength, Allocator.TempJob);

			if (entityLength > 0)
			{
				Profiler.BeginSample("Update Entities");
				ReadArchetypes(in baseline, in reader, ref ctx);

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
						if (ghostId == baseline.GhostIds[ghostIndex])
						{
							exists = true;
							break;
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

					// The archetype got changed
					if (EntityManager.GetComponentData<ReplicatedEntity>(targetWorldEntity).Archetype != archetype
					    // or it's a new entity...
					    || isNew)
					{
						ghostUpdate.Add(ghostId);
						entityUpdate.Add(targetWorldEntity);
						archetypeUpdate.Add(archetype);
					}
					// or if the ghost was sorted and is not at the same position...
					else if (ent != ghostIndex)
					{
						// we don't update the archetype nor the entity here
						ghostIndexUpdate.Add(ghostId);
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

					foreach (var systemId in m_DynamicSystemIds)
						m_SnapshotManager.CustomSerializer.RemoveGhost(systemId, ghostId);

					if (baseline.GhostToEntityMap.ContainsKey(ghostId))
						baseline.GhostToEntityMap.Remove(ghostId);

					var baselineEnt = baseline.Entities[ent];
					if (EntityManager.Exists(baselineEnt))
					{
						if (!EntityManager.HasComponent(baselineEnt, typeof(ManualDestroy)))
							EntityManager.DestroyEntity(baselineEnt);
						else
						{
							EntityManager.AddComponent(baselineEnt, typeof(IsDestroyedOnSnapshot));
							EntityManager.RemoveComponent(baselineEnt, typeof(ReplicatedEntity));
						}
					}
				}

				baseline.Entities.Clear();
				baseline.GhostIds.Clear();

				baseline.Entities.AddRange(newEntityArray);
				baseline.GhostIds.AddRange(newGhostArray);
				Profiler.EndSample();
			}
			else
			{
				var entityChangeCount = reader.ReadInt(ref ctx);
				if (entityChangeCount >= 0)
				{
					ReadArchetypes(in baseline, in reader, ref ctx);

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
				Profiler.BeginSample("Process Entity Update");
				foreach (var systemId in m_DynamicSystemIds)
				{
					m_SnapshotManager.CustomSerializer.ClearGhosts(systemId);
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
						m_SnapshotManager.CustomSerializer.AddGhost(sys, baseline.GhostIds[index]);
					}
				}

				foreach (var system in m_SnapshotManager.IdToSystems.Values)
					if (system is IDynamicSnapshotSystem dynamicSystem)
						dynamicSystem.OnDeserializerArchetypeUpdate(entityUpdate, archetypeUpdate, m_SnapshotManager.ArchetypeToSystems);
				Profiler.EndSample();
			}
			else if (ghostIndexUpdate.Length > 0)
			{
				foreach (var systemId in m_DynamicSystemIds)
				{
					m_SnapshotManager.CustomSerializer.ClearGhosts(systemId);
				}

				for (var index = 0; index < baseline.Entities.Length; index++)
				{
					var entity  = baseline.Entities[index];
					var repl    = EntityManager.GetComponentData<ReplicatedEntity>(entity);
					var systems = m_SnapshotManager.ArchetypeToSystems[repl.Archetype];

					foreach (var sys in systems)
					{
						m_SnapshotManager.CustomSerializer.AddGhost(sys, baseline.GhostIds[index]);
					}
				}
			}

			ghostUpdate.Dispose();
			ghostIndexUpdate.Dispose();
			entityUpdate.Dispose();
			archetypeUpdate.Dispose();

			var delegateGroup = World.GetExistingSystem<SnapshotWithDelegateSystemGroup>();

			Profiler.BeginSample("DelegateGroup.BeginDeserialize");
			delegateGroup.BeginDeserialize(baseline.Client, out var delegateDeserializers);
			Profiler.EndSample();

			var readCtxArray = new NativeArray<DataStreamReader.Context>(1, Allocator.TempJob)
			{
				[0] = ctx
			};
			m_SnapshotManager.CustomSerializer.Deserialize(baseline, delegateDeserializers, data, readCtxArray, debugRange);

			readCtxArray.Dispose();

			Profiler.BeginSample("Apply Snapshots Group");
			World.GetOrCreateSystem<AfterSnapshotIsAppliedSystemGroup>().ForceUpdate();
			Profiler.EndSample();
		}
	}
}
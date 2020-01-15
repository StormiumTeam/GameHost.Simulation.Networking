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
		private SnapshotManager                    m_SnapshotManager;
		private Dictionary<uint, NativeList<uint>> m_SystemToGhostIds;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_SnapshotManager = World.GetOrCreateSystem<SnapshotManager>();
			m_SystemToGhostIds = new Dictionary<uint, NativeList<uint>>();
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
					ref var sharedGhost = ref dynamicSystem.GetSharedGhost();
					if (!m_SystemToGhostIds.TryGetValue(system.Key, out var list))
					{
						list                           = new NativeList<uint>(64, Allocator.Persistent);
						m_SystemToGhostIds[system.Key] = list;
					}

					sharedGhost.Ghosts = list;
				}
			Profiler.EndSample();

			var debugRange = reader.ReadByte(ref ctx) == 1;
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

					foreach (var system in m_SystemToGhostIds)
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

					foreach (var sys in systems) m_SystemToGhostIds[sys].Add(baseline.GhostIds[index]);
				}

				foreach (var system in m_SnapshotManager.IdToSystems.Values)
					if (system is IDynamicSnapshotSystem dynamicSystem)
						dynamicSystem.OnDeserializerArchetypeUpdate(entityUpdate, archetypeUpdate, m_SnapshotManager.ArchetypeToSystems);
				Profiler.EndSample();
			}
			else if (ghostIndexUpdate.Length > 0)
			{
				foreach (var kvp in m_SystemToGhostIds)
				{
					var ghostList = kvp.Value;
					ghostList.Clear();
				}

				for (var index = 0; index < baseline.Entities.Length; index++)
				{
					var entity  = baseline.Entities[index];
					var repl    = EntityManager.GetComponentData<ReplicatedEntity>(entity);
					var systems = m_SnapshotManager.ArchetypeToSystems[repl.Archetype];

					foreach (var sys in systems) m_SystemToGhostIds[sys].Add(baseline.GhostIds[index]);
				}
			}

			ghostUpdate.Dispose();
			ghostIndexUpdate.Dispose();
			entityUpdate.Dispose();
			archetypeUpdate.Dispose();
			
			var delegateGroup         = World.GetExistingSystem<SnapshotWithDelegateSystemGroup>();

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

		[BurstCompile]
		public struct DeserializeJob : IJob
		{
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

			public void Execute()
			{
				var reader  = new DataStreamReader(StreamData);
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

						if (DebugRange)
						{
							var byteRead   = reader.GetBytesRead(ref parameters.Ctx);
							var currLength = reader.ReadInt(ref parameters.Ctx);
							if (currLength != byteRead)
							{
								ThrowError(currLength, byteRead, i, serializer);
								return;
							}
						}

						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}
				else
				{
					for (int i = 0, deserializeLength = Deserializers.Length; i < deserializeLength; i++)
					{
						var serializer = Deserializers[i];
						var invoke     = serializer.Value.Invoke;
						
						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}

				ReadContext[0] = parameters.Ctx;
			}
		}
	}
}
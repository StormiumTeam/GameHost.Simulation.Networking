using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;

namespace Revolution
{
	public class ReferencableSerializeClientData
	{
		public SerializeClientData Data;
	}
	
	public class CreateSnapshotSystem : ComponentSystem
	{
		private NativeHashMap<ArchetypeChunk, ArchData> m_ChunkToGhostArchetype;

		private Dictionary<Entity, ArchetypeChunk> m_EntityToChunk;

		private EntityQuery m_GhostEntityQuery;

		private uint                                 m_GhostId;
		private NativeQueue<uint>                    m_GhostIdQueue;
		private EntityQuery                          m_GhostWithoutIdentifierQuery;
		private EntityQuery                          m_InvalidGhostQuery;
		private SnapshotManager                      m_SnapshotManager;
		
		public NativeHashMap<uint, Entity> GhostToEntityMap;

		private DataStreamWriter writer;
		
		protected override void OnCreate()
		{
			base.OnCreate();

			m_SnapshotManager = World.GetOrCreateSystem<SnapshotManager>();

			GhostToEntityMap = new NativeHashMap<uint, Entity>(64, Allocator.Persistent);
			
			m_GhostEntityQuery = GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {typeof(GhostEntity), typeof(GhostIdentifier)}
			});
			m_GhostWithoutIdentifierQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(GhostEntity)},
				None = new ComponentType[] {typeof(GhostIdentifier)}
			});
			m_InvalidGhostQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(GhostIdentifier)},
				None = new ComponentType[] {typeof(GhostEntity)}
			});
			m_GhostId = 1;

			m_ChunkToGhostArchetype = new NativeHashMap<ArchetypeChunk, ArchData>(128, Allocator.Persistent);
			m_GhostIdQueue          = new NativeQueue<uint>(Allocator.Persistent);
			m_EntityToChunk         = new Dictionary<Entity, ArchetypeChunk>();
			
			writer = new DataStreamWriter(24256, Allocator.Persistent);
		}

		protected override void OnUpdate()
		{
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			GhostToEntityMap.Dispose();
			m_ChunkToGhostArchetype.Dispose();
			m_GhostIdQueue.Dispose();
			m_EntityToChunk.Clear();
		}

		private uint FindGhostId(NativeList<uint> blockedId)
		{
			if (m_GhostIdQueue.Count == 0)
				return m_GhostId++;
			if (!blockedId.Contains(m_GhostIdQueue.Peek()))
				return m_GhostIdQueue.Dequeue();
			return m_GhostId++;
		}

		private void CreateNewGhosts(NativeList<uint> blockedId)
		{
			if (m_InvalidGhostQuery.CalculateEntityCount() > 0)
			{
				using (var ghostArray = m_InvalidGhostQuery.ToComponentDataArray<GhostIdentifier>(Allocator.TempJob))
				{
					foreach (var id in ghostArray)
						m_GhostIdQueue.Enqueue(id.Value);
				}

				EntityManager.RemoveComponent<GhostIdentifier>(m_InvalidGhostQuery);
			}
			
			if (m_GhostWithoutIdentifierQuery.CalculateEntityCount() == 0)
				return;

			var addArray                                                           = new NativeArray<GhostIdentifier>(m_GhostWithoutIdentifierQuery.CalculateEntityCount(), Allocator.TempJob);
			for (int i = 0, length = addArray.Length; i < length; i++) addArray[i] = new GhostIdentifier {Value = FindGhostId(blockedId)};

			EntityManager.AddComponentData(m_GhostWithoutIdentifierQuery, addArray);

			addArray.Dispose();
		}

		/// <summary>
		///     Create a chained operation for a set of clients
		/// </summary>
		/// <param name="tick"></param>
		/// <param name="lookup"></param>
		public unsafe void CreateSnapshot(uint tick, in Dictionary<Entity, ReferencableSerializeClientData> lookup)
		{
			var blockedList = new NativeList<uint>(lookup.Count * 8, Allocator.Temp);
			foreach (var data in lookup.Values) blockedList.AddRange(data.Data.BlockedGhostIds);

			CreateNewGhosts(blockedList);

			var entities   = new NativeArray<Entity>(m_GhostEntityQuery.CalculateEntityCount(), Allocator.TempJob);
			var ghostArray = new NativeArray<GhostIdentifier>(m_GhostEntityQuery.CalculateEntityCount(), Allocator.TempJob);
			
			var chunks     = m_GhostEntityQuery.CreateArchetypeChunkArray(Allocator.TempJob);

			var x = 0;
			Profiler.BeginSample("Set GhostArray and EntityArray");
			
			GhostToEntityMap.Clear();
			foreach (var chunk in chunks)
			{
				var entityArray  = chunk.GetNativeArray(GetArchetypeChunkEntityType());
				var ghostIdArray = chunk.GetNativeArray(GetArchetypeChunkComponentType<GhostIdentifier>());
				for (var index = 0; index < ghostIdArray.Length; index++)
				{
					entities[x]                                 = entityArray[index];
					ghostArray[x]                               = ghostIdArray[index];
					GhostToEntityMap[ghostIdArray[index].Value] = entityArray[index];
					x++;
				}
			}

			Profiler.EndSample();

			Profiler.BeginSample("Init System Chunks Data");
			foreach (var systemKvp in m_SnapshotManager.IdToSystems)
			{
				var system = systemKvp.Value;
				if (system is IDynamicSnapshotSystem dynamicSystem)
				{
					m_SnapshotManager.CustomSerializer.ClearChunks(systemKvp.Key, dynamicSystem);
				}
			}

			Profiler.EndSample();

			Profiler.BeginSample("Check Entity Update");
			var entityUpdate = new NativeList<Entity>(entities.Length, Allocator.Temp);
			foreach (var entity in entities)
			{
				var currChunk = EntityManager.GetChunk(entity);
				if (m_EntityToChunk.TryGetValue(entity, out var otherChunk) && otherChunk == currChunk)
					continue;

				m_EntityToChunk[entity] = currChunk;
				entityUpdate.Add(entity);
			}

			Profiler.EndSample();

			var i = 0;
			Profiler.BeginSample("Search Chunk GhostArchetype");
			foreach (var chunk in chunks)
			{
				if (!m_ChunkToGhostArchetype.TryGetValue(chunk, out var archetype)
				    || archetype.EntityArch != chunk.Archetype)
				{
					var archetypeChanged = archetype.EntityArch != chunk.Archetype;
					var archId           = m_SnapshotManager.FindArchetype(chunk);
					/*if (archId <= 0) // todo: it is wrong if archId == empty?
						continue;*/

					m_ChunkToGhostArchetype[chunk] = archetype = new ArchData
					{
						EntityArch = chunk.Archetype,
						GhostArch  = archId
					};

					if (archetypeChanged)
					{
						var chunkEntities = chunk.GetNativeArray(GetArchetypeChunkEntityType());
						foreach (var ent in chunkEntities)
							if (!entityUpdate.Contains(ent))
								entityUpdate.Add(ent);
					}

					// Debug.Log($"Set archId={archId} -> {string.Join(",", archetype.EntityArch.GetComponentTypes())}");
				}

				var systemIdArray = m_SnapshotManager.ArchetypeToSystems[archetype.GhostArch];
				var systemIds     = (uint*) systemIdArray.GetUnsafePtr();
				var systemLength  = systemIdArray.Length;
				for (var sys = 0; sys != systemLength; sys++)
				{
					var system = m_SnapshotManager.GetSystem(systemIds[sys]);
					if (system is IDynamicSnapshotSystem dynamicSystem)
					{
						m_SnapshotManager.CustomSerializer.AddChunk(systemIds[sys], dynamicSystem, chunk);
					}
				}
			}

			Profiler.EndSample();
			
			Profiler.BeginSample("Create Snapshots");
			var outgoing = new NativeList<byte>(1024, Allocator.TempJob);
			foreach (var data in lookup)
			{
				var serializeData = data.Value;
				serializeData.Data.Client = data.Key;
				serializeData.Data.Tick   = tick;
				CreateSnapshot(outgoing, ref serializeData.Data, in chunks, in entities, in ghostArray, entityUpdate);

				var dBuffer = EntityManager.GetBuffer<ClientSnapshotBuffer>(data.Key);
				dBuffer.Clear();
				dBuffer.Reinterpret<byte>().AddRange(outgoing);

				outgoing.Clear();
			}

			outgoing.Dispose();
			Profiler.EndSample();

			entities.Dispose();
			ghostArray.Dispose();
			chunks.Dispose();
		}

		private void WriteArchetypes(in SerializeClientData baseline, ref DataStreamWriter writer, NativeArray<Entity> entities)
		{
			// Find archetype that are not already registered client-side
			var deferredArchetypeCount = writer.Write(0);
			var archetypeAdded         = 0;
			var previousArchetypeId    = 0u;
			foreach (var entity in entities)
			{
				var archetype = m_ChunkToGhostArchetype[EntityManager.GetChunk(entity)].GhostArch;
				var exists    = false;
				foreach (var arch in baseline.KnownArchetypes)
					if (arch == archetype)
					{
						exists = true;
						break;
					}

				if (exists)
					continue;

				writer.WritePackedUIntDelta(archetype, previousArchetypeId, baseline.NetworkCompressionModel);
				var systems = m_SnapshotManager.ArchetypeToSystems[archetype];
				writer.WritePackedUInt((uint) systems.Length, baseline.NetworkCompressionModel);
				for (var i = 0; i != systems.Length; i++) writer.WritePackedUInt(systems[i], baseline.NetworkCompressionModel);

				previousArchetypeId = archetype;

				baseline.KnownArchetypes.Add(archetype);
				archetypeAdded++;
			}

			writer.Write((byte) 42);
			deferredArchetypeCount.Update(archetypeAdded);
		}

		/// <summary>
		///     Create the snapshot
		/// </summary>
		/// <param name="outgoing"></param>
		/// <param name="baseline"></param>
		/// <param name="chunks"></param>
		/// <param name="entities"></param>
		/// <param name="ghostArray"></param>
		/// <param name="entityUpdate"></param>
		/// <param name="inChain"></param>
		/// <param name="inputDeps"></param>
		/// <returns></returns>
		/// <exception cref="NotImplementedException"></exception>
		/// <exception cref="InvalidOperationException"></exception>
		public unsafe void CreateSnapshot(NativeList<byte>       outgoing, ref SerializeClientData             baseline, in NativeArray<ArchetypeChunk> chunks,
		                                  in NativeArray<Entity> entities, in NativeArray<GhostIdentifier> ghostArray,
		                                  in NativeArray<Entity> entityUpdate,
		                                  bool                   inChain = true)
		{
			if (!inChain)
				throw new NotImplementedException("unchained operation for 'CreateSnapshot' is not available for now;");

			if (!baseline.TryGetSnapshot(0, out _)) baseline.CreateSnapshotFor(0);

			baseline.BeginSerialize(this, chunks);

			var debugRange = false;
			writer.Clear();
			writer.Write((byte) (debugRange ? 1 : 0)); // DEBUG RANGE

			// Before we write anything, we need to check if the ghosts are sorted correctly to not have problems client-side
			var deferredEntityCount = writer.Write(0);
			{
				var remake = baseline.ProgressiveGhostIds.Length != ghostArray.Length;
				if (!remake)
				{
					if (sizeof(GhostIdentifier) != sizeof(uint)) throw new InvalidOperationException("Size mismatch");

					remake = UnsafeUtility.MemCmp(ghostArray.GetUnsafePtr(), baseline.ProgressiveGhostIds.GetUnsafePtr(), sizeof(uint) * ghostArray.Length) != 0;
				}
				
				if (remake)
				{
					if (sizeof(GhostIdentifier) != sizeof(uint)) throw new InvalidOperationException("Size mismatch");

					// ----- ARCHETYPE PART ----- //
					WriteArchetypes(in baseline, ref writer, entities);
					deferredEntityCount.Update(ghostArray.Length);

					// ----- ENTITY PART ----- //

					// Check for any removed ghosts
					for (int ent = 0, length = baseline.ProgressiveGhostIds.Length; ent < length; ent++)
					{
						var ghost  = baseline.ProgressiveGhostIds[ent];
						var exists = false;
						for (int y = 0, count = ghostArray.Length; y < count; y++)
							if (ghost == ghostArray[y].Value)
							{
								exists = true;
								break;
							}

						if (exists)
							continue;

						// If it got deleted, block this id from being dequeued (until the client fully acknowledge this id is not used anymore)
						baseline.BlockedGhostIds.Add(ghost);
					}

					baseline.ProgressiveGhostIds.Clear();
					baseline.ProgressiveGhostIds.AddRange((uint*) ghostArray.GetUnsafePtr(), ghostArray.Length);


					var previousId        = 0u;
					var previousArchetype = 0u;

					uint ghostId = default;
					for (int i = 0, length = ghostArray.Length; i < length; i++)
					{
						ghostId = ghostArray[i].Value;

						writer.WritePackedUIntDelta(ghostId, previousId, baseline.NetworkCompressionModel);
						if (!baseline.TryGetSnapshot(ghostId, out _)) baseline.CreateSnapshotFor(ghostId);

						var archetype = m_ChunkToGhostArchetype[EntityManager.GetChunk(entities[i])].GhostArch;
						writer.WritePackedUIntDelta(archetype, previousArchetype, baseline.NetworkCompressionModel);

						previousArchetype = archetype;
						previousId        = ghostId;
					}
				}
				else if (entityUpdate.Length > 0)
				{
					var previousId           = 0u;
					var previousArchetype    = 0u;
					var deferredEntityChange = writer.Write(0);

					WriteArchetypes(in baseline, ref writer, entities);

					var changeCount = 0;
					for (int ent = 0, count = ghostArray.Length; ent < count; ent++)
					{
						var needUpdate = false;
						for (int u = 0, uCount = entityUpdate.Length; u < uCount; u++)
							if (entities[ent] == entityUpdate[u])
							{
								needUpdate = true;
								break;
							}

						if (!needUpdate)
							continue;

						var archetype = m_ChunkToGhostArchetype[EntityManager.GetChunk(entities[ent])].GhostArch;
						writer.WritePackedUIntDelta((uint) ent, previousId, baseline.NetworkCompressionModel);
						writer.WritePackedUIntDelta(archetype, previousArchetype, baseline.NetworkCompressionModel);

						previousId        = (uint) ent;
						previousArchetype = archetype;

						changeCount++;
					}

					writer.Write((byte) 42); // DON'T REMOVE THIS LINE
					deferredEntityChange.Update(changeCount);
				}
				else
				{
					writer.Write(-1);
				}
			}

			var delegateGroup = World.GetExistingSystem<SnapshotWithDelegateSystemGroup>();
			delegateGroup.BeginSerialize(baseline.Client, out var delegateSerializers);

			m_SnapshotManager.CustomSerializer.Serialize(baseline, delegateSerializers, writer, outgoing, debugRange);
		}

		private struct ArchData
		{
			public EntityArchetype EntityArch;
			public uint            GhostArch;
		}
	}
}
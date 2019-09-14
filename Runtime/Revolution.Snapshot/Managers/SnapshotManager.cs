using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution
{
	public class SnapshotWithDelegateSystemGroup : ComponentSystemGroup
	{
		public override void SortSystemUpdateList()
		{
			base.SortSystemUpdateList();
		}

		public void BeginSerialize(Entity client, ref NativeList<SortDelegate<OnSerializeSnapshot>> serializers)
		{
			foreach (var sys in m_systemsToUpdate)
			{
				var castSys = ((ISystemDelegateForSnapshot) sys);
				castSys.OnBeginSerialize(client);
				serializers.Add(new SortDelegate<OnSerializeSnapshot>
				{
					Value = castSys.SerializeDelegate,
					SystemId = (int) World.GetExistingSystem<SnapshotManager>().GetSystemId(castSys)
				});
			}
		}

		public void BeginDeserialize(Entity client, ref NativeList<SortDelegate<OnDeserializeSnapshot>> deserializers)
		{
			foreach (var sys in m_systemsToUpdate)
			{
				var castSys = ((ISystemDelegateForSnapshot) sys);
				castSys.OnBeginDeserialize(client);
				deserializers.Add(new SortDelegate<OnDeserializeSnapshot>
				{
					Value    = castSys.DeserializeDelegate,
					SystemId = (int) World.GetExistingSystem<SnapshotManager>().GetSystemId(castSys)
				});
			}
		}
	}

	public struct SharedSystemChunk
	{
		public NativeList<ArchetypeChunk>  Chunks;
		public NativeArray<ArchetypeChunk> Array => Chunks;
	}

	public struct SharedSystemGhost
	{
		public NativeList<uint>  Ghosts;
		public NativeArray<uint> Array => Ghosts;
	}
	
	public delegate void OnSerializeSnapshot(uint systemId, ref SerializeClientData serializeData, ref DataStreamWriter writer);

	public delegate void OnDeserializeSnapshot(uint systemId, uint tick, ref DeserializeClientData deserializeData, ref DataStreamReader reader, ref DataStreamReader.Context ctx);

	public class SnapshotManager : ComponentSystem
	{
		private bool                     m_IsDynamicSystemId;
		private bool m_IsAddingToFixedCollection;
		
		public  Dictionary<uint, object> IdToSystems;
		public Dictionary<object, uint> SystemsToId;
		
		public Dictionary<uint, NativeArray<uint>> ArchetypeToSystems;

		protected override void OnCreate()
		{
			base.OnCreate();
			
			IdToSystems = new Dictionary<uint, object>
			{
				[0] = null
			};
			SystemsToId = new Dictionary<object, uint>();
			m_IsDynamicSystemId = true;

			ArchetypeToSystems    = new Dictionary<uint, NativeArray<uint>>(64);
			ArchetypeToSystems[0] = new NativeArray<uint>(0, Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			IdToSystems.Clear();
			SystemsToId.Clear();
			foreach (var kvp in ArchetypeToSystems)
			{
				kvp.Value.Dispose();
			}

			ArchetypeToSystems.Clear();
		}

		/// <summary>
		/// Set a fixed collection of system.
		/// </summary>
		/// <param name="systems"></param>
		public void SetFixedSystems(Dictionary<uint, object> systems)
		{
			if (systems[0] != null)
				throw new InvalidOperationException("The first element in the systems dictionary should be null.");
			
			m_IsDynamicSystemId = false;
			IdToSystems       = systems;
			SystemsToId.Clear();
			foreach (var idToSystem in IdToSystems)
			{
				if (idToSystem.Value == null)
					continue;
				
				SystemsToId[idToSystem.Value] = idToSystem.Key;
			}
		}

		public void SetFixedSystemsFromBuilder(Action<World, CollectionBuilder<object>> builder)
		{
			var cb = new CollectionBuilder<object>();
			cb.Set(0, null);

			m_IsDynamicSystemId = false;
			m_IsAddingToFixedCollection = true;
			try
			{
				builder(World, cb);
				SetFixedSystems(cb.Build());
			}
			catch (Exception ex)
			{
				Debug.LogException(ex);
			}
			finally
			{
				m_IsAddingToFixedCollection = false;
			}
		}

		/// <summary>
		/// Set the system to be dynamic
		/// </summary>
		public void SetDynamicSystems()
		{
			m_IsDynamicSystemId = false;
		}

		/// <summary>
		/// Register a system
		/// </summary>
		/// <param name="system"></param>
		/// <typeparam name="T"></typeparam>
		public void RegisterSystem<T>(T system)
		{
			if (!m_IsDynamicSystemId && !m_IsAddingToFixedCollection)
			{
				if (!IdToSystems.ContainsValue(system))
				{
					Debug.LogError($"We couldn't add {system} since it use a fixed system list.");
					return;
				}
			}

			var id = (uint) IdToSystems.Count;
			IdToSystems.Add(id, system);
			SystemsToId.Add(system, id);
		}

		public T GetSystem<T>(uint id)
		{
			return (T) IdToSystems[id];
		}

		public object GetSystem(uint id)
		{
			return IdToSystems[id];
		}

		private void FindSystemForChunk(ArchetypeChunk chunk, NativeList<uint> systems)
		{
			foreach (var sysKvp in IdToSystems)
			{
				if (sysKvp.Value is IDynamicSnapshotSystem dynamicSystem
				    && dynamicSystem.IsChunkValid(chunk))
				{
					systems.Add(sysKvp.Key);
				}
			}
		}

		// TODO: THIS METHOD SHALL BE REMOVED
		public void ForceSetArchetype(uint index, NativeArray<uint> systems)
		{
			if (ArchetypeToSystems.ContainsKey(index))
			{
				ArchetypeToSystems[index].Dispose();
			}
			ArchetypeToSystems[(uint) index] = systems;
		}

		private uint CreateArchetype(NativeArray<uint> systems)
		{
			var index = (uint) ArchetypeToSystems.Count;
			ArchetypeToSystems[(uint) index] = systems;

			return index;
		}

		public uint FindArchetype(ArchetypeChunk chunk)
		{
			var systems = new NativeList<uint>(IdToSystems.Count, Allocator.Temp);
			FindSystemForChunk(chunk, systems);
			if (systems.Length <= 0)
				return 0;

			var systemArray = (NativeArray<uint>) systems;

			foreach (var archetype in ArchetypeToSystems)
			{
				var matches = 0;
				var models  = archetype.Value;
				for (int i = 0, length = models.Length; i < length; i++)
				{
					for (int j = 0, count = systemArray.Length; j < count; j++)
					{
						if (models[i] == systemArray[j]) matches++;
					}
				}

				if (matches == systemArray.Length)
					return archetype.Key;
			}

			// We need to create our own archetype
			var copy = new NativeArray<uint>(systems.Length, Allocator.Persistent);
			copy.CopyFrom(systems);
			
			return CreateArchetype(copy);
		}

		protected override void OnUpdate()
		{

		}

		public ulong GetSystemId(object obj)
		{
			return SystemsToId[obj];
		}
	}
}
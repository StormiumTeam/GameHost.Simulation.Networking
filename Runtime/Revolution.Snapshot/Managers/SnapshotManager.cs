using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;
using UnityEngine.Profiling;

namespace Revolution
{
	public class SnapshotWithDelegateSystemGroup : ComponentSystemGroup
	{
		private List<ISystemDelegateForSnapshot> m_DelegateSystems = new List<ISystemDelegateForSnapshot>();
		private NativeList<SortDelegate<OnDeserializeSnapshot>> m_OnDeserializeList;
		private NativeList<SortDelegate<OnSerializeSnapshot>> m_OnSerializeList;
		
		private SnapshotManager snapshotMgr;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_OnDeserializeList = new NativeList<SortDelegate<OnDeserializeSnapshot>>(Allocator.Persistent);
			m_OnSerializeList   = new NativeList<SortDelegate<OnSerializeSnapshot>>(Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			m_OnDeserializeList.Dispose();
			m_OnSerializeList.Dispose();
		}

		public override void SortSystemUpdateList()
		{
			base.SortSystemUpdateList();

			snapshotMgr = World.GetExistingSystem<SnapshotManager>();

			m_DelegateSystems.Clear();
			m_OnDeserializeList.Clear();
			m_OnSerializeList.Clear();
			foreach (var sys in Systems)
			{
				if (sys is ISystemDelegateForSnapshot castSys)
				{
					m_DelegateSystems.Add(castSys);
					m_OnDeserializeList.Add(new SortDelegate<OnDeserializeSnapshot>
					{
						Name     = castSys.NativeName,
						SystemId = (int) snapshotMgr.GetSystemId(sys),
						Value    = castSys.DeserializeDelegate
					});
					m_OnSerializeList.Add(new SortDelegate<OnSerializeSnapshot>
					{
						Name     = castSys.NativeName,
						SystemId = (int) snapshotMgr.GetSystemId(sys),
						Value    = castSys.SerializeDelegate
					});
				}
			}
		}

		public void BeginSerialize(Entity client, out NativeList<SortDelegate<OnSerializeSnapshot>> serializers)
		{
			foreach (var sys in m_DelegateSystems)
			{
				sys.OnBeginSerialize(client);
				/*serializers.Add(new SortDelegate<OnSerializeSnapshot>
				{
					Value    = sys.SerializeDelegate,
					SystemId = (int) snapshotMgr.GetSystemId(sys)
				});*/
			}

			serializers = m_OnSerializeList;
		}

		public void BeginDeserialize(Entity client, out NativeList<SortDelegate<OnDeserializeSnapshot>> deserializers)
		{
			foreach (var sys in m_DelegateSystems)
			{
				sys.OnBeginDeserialize(client);
				/*deserializers.Add(new SortDelegate<OnDeserializeSnapshot>
				{
					Name     = sys.NativeName,
					Value    = sys.DeserializeDelegate,
					SystemId = (int) snapshotMgr.GetSystemId(sys)
				});*/
			}

			deserializers = m_OnDeserializeList;
		}
	}

	public struct SharedSystemChunk
	{
		public uint                        SystemId;
		public NativeList<ArchetypeChunk>  Chunks;
		public NativeArray<ArchetypeChunk> Array => Chunks;
	}

	public struct SharedSystemGhost
	{
		public uint              SystemId;
		public NativeList<uint>  Ghosts;
		public NativeArray<uint> Array => Ghosts;
	}

	public unsafe struct Blittable<T>
	{
		private IntPtr m_Ptr;

		public ref T Value => ref Unsafe.AsRef<T>(m_Ptr.ToPointer());

		public Blittable(ref T value)
		{
			m_Ptr = (IntPtr) Unsafe.AsPointer(ref value);
		}
	}

	public struct SerializeParameters
	{
		internal Blittable<SerializeClientData> m_ClientData;
		internal Blittable<DataStreamWriter>    m_Stream;

		public uint SystemId;

		public ref SerializeClientData ClientData => ref m_ClientData.Value;
		public ref DataStreamWriter    Stream     => ref m_Stream.Value;

		public uint                    Tick                    => ClientData.Tick;
		public NetworkCompressionModel NetworkCompressionModel => ClientData.NetworkCompressionModel;
	}

	public struct DeserializeParameters
	{
		internal Blittable<DeserializeClientData> m_ClientData;

		public uint                     SystemId;
		public DataStreamReader         Stream;
		public DataStreamReader.Context Ctx;

		public ref DeserializeClientData ClientData => ref m_ClientData.Value;

		public uint                    Tick                    => ClientData.Tick;
		public NetworkCompressionModel NetworkCompressionModel => ClientData.NetworkCompressionModel;
	}

	public delegate void OnSerializeSnapshot(ref SerializeParameters parameters);

	public delegate void OnDeserializeSnapshot(ref DeserializeParameters parameters);

	public class SnapshotManager : ComponentSystem
	{
		public Dictionary<uint, NativeArray<uint>> ArchetypeToSystems;

		public FastDictionary<uint, object> IdToSystems;
		private bool                     m_IsAddingToFixedCollection;
		private bool                     m_IsDynamicSystemId;
		public  FastDictionary<object, uint> SystemsToId;

		protected override void OnCreate()
		{
			base.OnCreate();

			IdToSystems = new FastDictionary<uint, object>
			{
				[0] = null
			};
			SystemsToId         = new FastDictionary<object, uint>();
			m_IsDynamicSystemId = true;

			ArchetypeToSystems    = new Dictionary<uint, NativeArray<uint>>(64);
			ArchetypeToSystems[0] = new NativeArray<uint>(0, Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			IdToSystems.Clear();
			SystemsToId.Clear();
			foreach (var kvp in ArchetypeToSystems) kvp.Value.Dispose();

			ArchetypeToSystems.Clear();
		}

		/// <summary>
		///     Set a fixed collection of system.
		/// </summary>
		/// <param name="systems"></param>
		public void SetFixedSystems(FastDictionary<uint, object> systems)
		{
			if (systems[0] != null)
				throw new InvalidOperationException("The first element in the systems dictionary should be null.");

			m_IsDynamicSystemId = false;
			IdToSystems         = systems;
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

			m_IsDynamicSystemId         = false;
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
		///     Set the system to be dynamic
		/// </summary>
		public void SetDynamicSystems()
		{
			m_IsDynamicSystemId = false;
		}

		/// <summary>
		///     Register a system
		/// </summary>
		/// <param name="system"></param>
		/// <typeparam name="T"></typeparam>
		public void RegisterSystem<T>(T system)
		{
			if (!m_IsDynamicSystemId && !m_IsAddingToFixedCollection)
				if (!IdToSystems.ContainsValue(system))
				{
					Debug.LogError($"We couldn't add {system} since it use a fixed system list.");
					return;
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
				if (sysKvp.Value is IDynamicSnapshotSystem dynamicSystem
				    && dynamicSystem.IsChunkValid(chunk))
					systems.Add(sysKvp.Key);
		}

		// TODO: THIS METHOD SHALL BE REMOVED
		public void ForceSetArchetype(uint index, NativeArray<uint> systems)
		{
			if (ArchetypeToSystems.ContainsKey(index)) ArchetypeToSystems[index].Dispose();
			ArchetypeToSystems[index] = systems;
		}

		private uint CreateArchetype(NativeArray<uint> systems)
		{
			var index = (uint) ArchetypeToSystems.Count;
			ArchetypeToSystems[index] = systems;

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
				if (models.Length != systemArray.Length)
					continue;

				for (int i = 0, length = models.Length; i < length; i++)
				for (int j = 0, count = systemArray.Length; j < count; j++)
					if (models[i] == systemArray[j])
						matches++;

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
using System;
using System.Collections.Generic;
using Collections.Pooled;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	/// <summary>
	/// The root contains all static information of frame children
	/// </summary>
	public class SnapshotRoot
	{
		private PooledList<uint> archetypes = new()
		{
			0 // Default Empty Archetype
		};

		private Dictionary<uint, PooledList<uint>> archetypeSystems = new()
		{
			{ 0, new() } // Default Empty Archetype
		};

		// fast path for converting gameWorld entity archetypes to snapshot archetypes
		private Dictionary<EntityArchetype, uint> gwArchetypeToSnapshotArchetype = new();

		public Span<uint> Archetypes => archetypes.Span;

		public Span<uint> GetArchetypeSystems(uint archetype) => archetypeSystems[archetype].Span;

		public uint ConvertFromGameWorldArchetype(EntityArchetype archetype)
		{
			gwArchetypeToSnapshotArchetype.TryGetValue(archetype, out var id);
			return id;
		}

		public uint GetOrCreateArchetype(Span<uint> systemIds)
		{
			foreach (var (idx, list) in archetypeSystems)
			{
				if (list.Span.SequenceEqual(systemIds))
					return idx;
			}

			var index = (uint)archetypes.Count;
			ReplaceArchetype(index, systemIds);

			return index;
		}

		public void ReplaceArchetype(uint archetype, Span<uint> systemIds)
		{
			if (!archetypeSystems.ContainsKey(archetype))
				archetypes.Add(archetype);

			archetypeSystems[archetype] = new(systemIds);
		}

		public void SetGameWorldArchetypeLink(EntityArchetype gwArchetype, uint snapshotArchetype)
		{
			gwArchetypeToSnapshotArchetype[gwArchetype] = snapshotArchetype;
		}
	}
}
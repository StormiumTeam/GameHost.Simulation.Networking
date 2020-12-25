using System;
using System.Collections.Generic;
using Collections.Pooled;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public interface ISnapshotState
	{
		/// <summary>
		///     The tick of this current state
		/// </summary>
		public uint Tick { get; }

		public uint       GetArchetypeOfEntity(uint entity);
		public Span<uint> GetArchetypeSystems(uint  archetype);
		public Span<uint> GetArchetypes();

		/// <summary>
		///     Get entities that were created by us
		/// </summary>
		public Span<uint> GetCreatedEntities();

		/// <summary>
		///     Get entities that are owned by us
		/// </summary>
		public Span<uint> GetOwnedEntities();

		/// <summary>
		///     Get all entities of this snapshot
		/// </summary>
		public Span<uint> GetAllEntities();

		/// <summary>
		/// Convert a local entity (snapshot) to self (on this world)
		/// </summary>
		public GameEntity LocalToSelf(GameEntity local);

		/// <summary>
		///     Do we own this entity?
		/// </summary>
		public bool Own(GameEntity local, SnapshotEntity remote);
	}

	public class SnapshotWriterState : ISnapshotState
	{
		private readonly PooledList<uint>         addedArchetypes    = new();
		private readonly Dictionary<uint, uint[]> archetypeToSystems = new();
		private readonly PooledList<uint>         totalArchetypes    = new();
		protected        bool[]                   archetypeAssigned;
		protected        uint[]                   authorityArchetypes;
		protected        uint[]                   archetypes;
		protected        bool[]                   archetypeUpdate;
		protected        uint[]                   localArchetypes;

		private   bool   canRegisterEntityAndArchetype;
		protected bool[] created;
		protected bool[] owned;

		private   int    previousEntityCount;
		protected bool[] registered;

		protected bool[] serialized;
		protected uint[] versions;

		public SnapshotWriterState()
		{
			archetypeToSystems[0] = Array.Empty<uint>();
			previousEntityCount   = -1;
		}

		public uint Tick { get; set; }

		public uint GetArchetypeOfEntity(uint entity)
		{
			return archetypes[entity];
		}

		public uint GetAuthorityArchetypeOfEntity(uint entity)
		{
			return authorityArchetypes[entity];
		}

		public Span<uint> GetArchetypeSystems(uint archetype)
		{
			return archetypeToSystems[archetype];
		}

		public Span<uint> GetArchetypes()
		{
			return totalArchetypes.Span;
		}

		public virtual Span<uint> GetCreatedEntities()
		{
			throw new NotImplementedException();
		}

		public virtual Span<uint> GetOwnedEntities()
		{
			throw new NotImplementedException();
		}

		public virtual Span<uint> GetAllEntities()
		{
			throw new NotImplementedException();
		}

		public GameEntity LocalToSelf(GameEntity local)
		{
			// We don't have this entity being serialized
			if (!registered[local.Id])
				return default;
			
			return local;
		}

		public virtual bool Own(GameEntity local, SnapshotEntity remote)
		{
			return owned[local.Id];
		}

		public void Prepare(int entityCount)
		{
			addedArchetypes.Clear();

			canRegisterEntityAndArchetype = true;

			if (entityCount > previousEntityCount)
			{
				previousEntityCount = entityCount;

				Array.Resize(ref serialized, entityCount);
				Array.Resize(ref registered, entityCount);
				Array.Resize(ref created, entityCount);
				Array.Resize(ref owned, entityCount);
				Array.Resize(ref archetypes, entityCount);
				Array.Resize(ref authorityArchetypes, entityCount);
				Array.Resize(ref archetypeAssigned, entityCount);
				Array.Resize(ref archetypeUpdate, entityCount);
				Array.Resize(ref localArchetypes, entityCount);
				Array.Resize(ref versions, entityCount);
			}

			Array.Fill(serialized, false);
		}

		public bool RegisterEntity(GameEntity entity, bool created, bool owned)
		{
			this.created[entity.Id] = created;
			this.owned[entity.Id]   = owned;

			serialized[entity.Id] = true;

			if (!registered[entity.Id] || versions[entity.Id] != entity.Version)
			{
				// When finalizing, it will set registered to true, and will so create an event, and add the entities in order.
				// Note that we force registered to be false here, it only happen when it's a new version.
				// Note² that the client should know that we switched version (and should delete the previous entity)
				// Note^3 in the future I'll give a better way for events
				registered[entity.Id] = false;
				versions[entity.Id]   = entity.Version;
				return true;
			}

			return false;
		}

		public bool AreSameLocalArchetype(GameEntity entity, in uint localArchetype)
		{
			return localArchetypes[entity.Id] == localArchetype;
		}

		public bool TryGetArchetypeFromEntity(GameEntity entity, out uint snapshotArchetype)
		{
			var arch = archetypes[entity.Id];
			snapshotArchetype = arch;
			return archetypeAssigned[entity.Id];
		}

		public void ClearAllAssignedArchetype()
		{
			// ReSharper disable ConditionIsAlwaysTrueOrFalse
			if (archetypes == null)
				return;
			// ReSharper restore ConditionIsAlwaysTrueOrFalse

			Array.Fill(archetypes, default);
			Array.Fill(archetypeUpdate, false);
			Array.Fill(archetypeAssigned, false);
			
			archetypeToSystems.Clear();
		}

		public void AssignSnapshotArchetype(GameEntity entity, uint snapshotArchetype, uint authorityArchetype, uint localArchetype)
		{
			if (!archetypeToSystems.ContainsKey(snapshotArchetype))
				throw new InvalidOperationException($"network archetype {snapshotArchetype} not registered (perhaps it's a local one?)");

			localArchetypes[entity.Id]     = localArchetype;
			authorityArchetypes[entity.Id] = authorityArchetype;

			if (archetypes[entity.Id] == snapshotArchetype && archetypeAssigned[entity.Id])
				return;

			archetypes[entity.Id]        = snapshotArchetype;
			archetypeUpdate[entity.Id]   = true;
			archetypeAssigned[entity.Id] = true;
		}

		public void FinalizeRegister((PooledList<GameEntity> total, PooledList<GameEntity> updated, PooledList<GameEntity> removed) entity,
		                             (PooledList<uint> total, PooledList<uint> added)                                               archetype)
		{
			canRegisterEntityAndArchetype = false;

			// Check for entities that should be removed.
			// Start a 1 since 0 == null
			for (var i = 1u; i != previousEntityCount; i++)
				// This entity is not serialized, but it was registered before
				// So create a remove event.
				if (!serialized[i] && registered[i])
				{
					entity.removed.Add(new GameEntity(i, versions[i]));
					registered[i] = false;
				}
				// This entity is gonna be serialized, so finalize registering
				else if (serialized[i])
				{
					// If either it's not yet officially registered, or the entity archetype was updated, create an update event. 
					if (!registered[i] || archetypeUpdate[i])
					{
						registered[i]      = true;
						archetypeUpdate[i] = false;
						entity.updated.Add(new GameEntity(i, versions[i]));
					}

					entity.total.Add(new GameEntity(i, versions[i]));
				}

			archetype.added.AddRange(addedArchetypes);
			archetype.total.AddRange(archetypeToSystems.Keys);
		}

		public uint CreateArchetype(ReadOnlySpan<uint> systems)
		{
			if (!canRegisterEntityAndArchetype)
				throw new InvalidOperationException("can not register archetype");

			var id = (uint) archetypeToSystems.Count;
			archetypeToSystems[id] = systems.ToArray();
			addedArchetypes.Add(id);
			totalArchetypes.Add(id);

			return id;
		}

		public bool TryGetArchetypeWithSystems(ReadOnlySpan<uint> systems, out uint archetype)
		{
			foreach (var (arch, archSystems) in archetypeToSystems)
				if (systems.SequenceEqual(archSystems))
				{
					archetype = arch;
					return true;
				}

			archetype = default;
			return false;
		}
	}
}
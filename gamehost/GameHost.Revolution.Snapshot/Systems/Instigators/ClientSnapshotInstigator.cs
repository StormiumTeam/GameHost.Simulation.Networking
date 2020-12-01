using System;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	[Flags]
	public enum ClientSnapshotPermission
	{
		/// <summary>
		///     This client can create entities
		/// </summary>
		CreateEntity = 0b0000_0001,

		/// <summary>
		///     This client can destroy their created entities (not owned)
		/// </summary>
		DestroyCreatedEntity = 0b0000_0010,

		/// <summary>
		///     This client can modify the archetype of owned entities
		/// </summary>
		ModifyArchetypeOfOwnedEntity = 0b0000_0100,

		/// <summary>
		///     This client can destroy owned entities (not created)
		/// </summary>
		DestroyOwnedEntity = 0b0000_1000,

		/// <summary>
		///     All permissions (mostly used for disallowing all permissions)
		/// </summary>
		All = CreateEntity | DestroyCreatedEntity | ModifyArchetypeOfOwnedEntity | DestroyOwnedEntity
	}

	public readonly struct ClientOwnedEntity
	{
		public readonly GameEntity               Entity;
		/// <summary>
		/// Represent which components can be modified by this entity
		/// </summary>
		public readonly uint                     ComponentDataArchetype;
		/// <summary>
		/// Get the permission on this entity
		/// </summary>
		/// <remarks>
		/// <see cref="ClientSnapshotPermission.CreateEntity"/> will not work.
		/// </remarks>
		public readonly ClientSnapshotPermission Permission;

		public ClientOwnedEntity(GameEntity entity, uint componentDataArchetype, ClientSnapshotPermission permission)
		{
			Entity                 = entity;
			ComponentDataArchetype = componentDataArchetype;
			Permission             = permission;
		}
	}

	public partial class ClientSnapshotInstigator : ISnapshotInstigator
	{
		private readonly Deserialization deserialization = new();
		private readonly GameWorld       gameWorld;

		public ClientSnapshotInstigator(Entity storage, int groupId, int parentGroupId, GameWorld gameWorld)
		{
			InstigatorId = groupId;
			ParentInstigatorId = parentGroupId;

			storage.Set(new ClientData());

			Storage = storage;

			this.gameWorld = gameWorld;
			clientState    = new ClientState();
			State          = new ClientSnapshotState(InstigatorId);
			OwnedEntities  = new PooledList<ClientOwnedEntity>();
		}

		public PooledDictionary<uint, ISerializer> Serializers   { get; set; }
		/// <summary>
		/// A client can possess entities that were not created by itself.
		/// </summary>
		public PooledList<ClientOwnedEntity>       OwnedEntities { get; }

		public Entity         Storage { get; }
		public ISnapshotState State   { get; }

		public int ParentInstigatorId { get; }
		public int InstigatorId { get; }

		public void Deserialize(Span<byte> data)
		{
			deserialization.Deserialize(this, data);
		}

		#region Preparation

		private          bool        isPreparing;
		private readonly ClientState clientState;

		public void Prepare()
		{
			if (isPreparing)
				throw new InvalidOperationException();

			isPreparing = true;
		}

		public ClientState GetClientState(out bool isPreparing)
		{
			isPreparing = this.isPreparing;
			return clientState;
		}

		public ClientData GetClientData()
		{
			return Storage.Get<ClientData>();
		}

		public void StopPrepare()
		{
			isPreparing = false;
		}

		#endregion
	}

	public sealed class ClientData : BitBuffer
	{
	}

	public class ClientState
	{
		public enum EOperation
		{
			/// <summary>
			///     No operation to execute
			/// </summary>
			None,

			/// <summary>
			///     Recreate the full list of entities for the client
			/// </summary>
			RecreateFull
		}

		public EOperation Operation = EOperation.None;
	}

	public class ClientSnapshotState : ISnapshotState
	{
		public readonly int              InstigatorId;
		public          uint[]           archetype;
		public          PooledList<uint> archetypes = new();

		public Dictionary<uint, uint[]> archetypeToSystems = new();
		public bool[]                   created;

		public PooledList<uint> createdEntities = new();
		public PooledList<uint> entities        = new();
		public bool[]           parentOwned;
		public bool[]           owned;

		public PooledList<uint> ownedEntities = new();

		private int              previousEntityCount;
		public  SnapshotEntity[] remote;

		public GameEntity[] snapshot;

		public Dictionary<GameEntity, GameEntity> snapshotToSelf = new();

		public ClientSnapshotState(int instigatorId)
		{
			InstigatorId = instigatorId;
		}

		public uint Tick { get; set; }

		public uint GetArchetypeOfEntity(uint entity)
		{
			return archetype[entity];
		}

		public Span<uint> GetArchetypeSystems(uint snapshotArchetype)
		{
			return archetypeToSystems[snapshotArchetype];
		}

		public Span<uint> GetArchetypes()
		{
			return archetypes.Span;
		}

		public Span<uint> GetCreatedEntities()
		{
			return createdEntities.Span;
		}

		public Span<uint> GetOwnedEntities()
		{
			return ownedEntities.Span;
		}

		public Span<uint> GetAllEntities()
		{
			return entities.Span;
		}

		public bool Own(GameEntity local, SnapshotEntity remote)
		{
			return owned[local.Id] || remote.Instigator == InstigatorId;
		}

		public void Prepare()
		{
			entities.Clear();
		}

		private void IncreaseCapacity(int entityCount)
		{
			if (entityCount > previousEntityCount)
			{
				previousEntityCount = entityCount;

				Array.Resize(ref snapshot, entityCount);
				Array.Resize(ref remote, entityCount);
				Array.Resize(ref created, entityCount);
				Array.Resize(ref parentOwned, entityCount);
				Array.Resize(ref owned, entityCount);
				Array.Resize(ref archetype, entityCount);
			}
		}

		public GameEntity GetSelfEntity(GameEntity snapshotLocal)
		{
			snapshotToSelf.TryGetValue(snapshotLocal, out var self);
			return self;
		}

		public void RemoveEntity(GameEntity self)
		{
			snapshotToSelf.Remove(snapshot[self.Id]);

			created[self.Id]  = false;
			owned[self.Id]    = false;
			snapshot[self.Id] = default;
			remote[self.Id]   = default;
		}

		/// <summary>
		/// Add an entity to this snapshot state.
		/// </summary>
		/// <param name="self">The self (aka on this application) version of the entity</param>
		/// <param name="snapshotLocal">The entity identifier on the snapshot</param>
		/// <param name="snapshotRemote">The entity identifier from the origin</param>
		/// <param name="isParentOwned">Is this entity owned by the parent of this client?</param>
		/// <param name="isOwned">Is this client owning the entity?</param>
		public void AddEntity(GameEntity self, GameEntity snapshotLocal, SnapshotEntity snapshotRemote, bool isParentOwned, bool isOwned)
		{
			IncreaseCapacity((int) self.Id + 1);

			if (snapshotRemote.Instigator == InstigatorId)
			{
				created[self.Id] = true;
				owned[self.Id]   = true;
			}

			parentOwned[self.Id]          = isParentOwned;
			owned[self.Id]                = isOwned;
			snapshot[self.Id]             = snapshotLocal;
			remote[self.Id]               = snapshotRemote;
			snapshotToSelf[snapshotLocal] = self;
		}

		public void FinalizeEntities()
		{
			for (var i = 1u; i < previousEntityCount; i++)
				if (snapshot[i] != default)
					entities.Add(i);
		}

		public void SetArchetypeSystems(uint snapshotArchetype, ReadOnlySpan<uint> systemIds)
		{
			archetypeToSystems[snapshotArchetype] = systemIds.ToArray();
			if (!archetypes.Contains(snapshotArchetype))
				archetypes.Add(snapshotArchetype);
		}

		public void AssignArchetype(uint entity, uint snapshotArchetype)
		{
			archetype[entity] = snapshotArchetype;
		}
	}
}
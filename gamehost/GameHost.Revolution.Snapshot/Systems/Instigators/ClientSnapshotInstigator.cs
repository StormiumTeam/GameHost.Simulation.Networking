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

		private readonly BroadcastInstigator parent;

		public ClientSnapshotInstigator(Entity storage, int groupId, BroadcastInstigator parent, GameWorld gameWorld)
		{
			InstigatorId = groupId;
			ParentInstigatorId = parent.InstigatorId;

			this.parent = parent;

			storage.Set(new ClientData());
			storage.Set<ISnapshotInstigator>(this);

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

			isPreparing           = true;
			//clientState.Operation = ClientState.EOperation.RecreateFull;
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
		public struct GhostInformation
		{
			public uint Archetype;
			public bool ParentOwned;
			public bool ParentDestroyed;
			public bool Owned;
			public uint OwnedArchetype;
			public bool IsDataIgnored;

			public GameEntity     Local;
			public GameEntity     Self;
			public SnapshotEntity Remote;
			
			public bool           IsInitialized => Local.Id > 0;
		}

		public readonly int              InstigatorId;
		
		public PooledList<uint>         archetypes         = new();
		public Dictionary<uint, uint[]> archetypeToSystems = new();
		
		public PooledList<uint> createdEntities = new();
		public PooledList<uint> entities        = new();
		public PooledList<uint> ownedEntities   = new();

		private int              previousEntityCount;

		public GhostInformation[]                 ghosts;
		public Dictionary<GameEntity, GameEntity> selfToSnapshot = new();

		public ClientSnapshotState(int instigatorId)
		{
			InstigatorId = instigatorId;

			IncreaseCapacity(16);
		}

		public uint Tick { get; set; }

		public uint GetArchetypeOfEntity(uint entity)
		{
			return ghosts[entity].Archetype;
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

		public GameEntity LocalToSelf(GameEntity local)
		{
			return ghosts[local.Id].Self;
		}

		public bool Own(GameEntity local, SnapshotEntity remote)
		{
			return ghosts[local.Id].Owned || remote.Instigator == InstigatorId;
		}

		public void Prepare(uint maxId)
		{
			entities.Clear();
			IncreaseCapacity((int) maxId);
		}

		public void IncreaseCapacity(int entityCount)
		{
			if (entityCount > previousEntityCount)
			{
				previousEntityCount = entityCount;

				Array.Resize(ref ghosts, entityCount);
			}
		}

		public ref GhostInformation GetRefGhost(uint id)
		{
			return ref ghosts[id];
		}

		public void RemoveGhost(uint id)
		{
			selfToSnapshot.Remove(ghosts[id].Self);
			ghosts[id] = default;
		}

		/// <summary>
		/// Add an entity to this snapshot state.
		/// </summary>
		/// <param name="self">The self (aka on this application) version of the entity</param>
		/// <param name="snapshotLocal">The entity identifier on the snapshot</param>
		/// <param name="snapshotRemote">The entity identifier from the origin</param>
		/// <param name="isParentOwned">Is this entity owned by the parent of this client?</param>
		/// <param name="isOwned">Is this client owning the entity?</param>
		/// <param name="isParentDestroyed">Is this entity destroyed on the parent?</param>
		public void AddEntity(GameEntity self, GameEntity snapshotLocal, SnapshotEntity snapshotRemote, bool isParentOwned, bool isOwned, bool isParentDestroyed)
		{
			GhostInformation ghost;
			ghost.OwnedArchetype = default;

			if (snapshotRemote.Instigator == InstigatorId)
			{
				ghost.Owned = true;
			}
			else
				ghost.Owned = isOwned;

			ghost.Archetype = 0;

			ghost.ParentOwned     = isParentOwned;
			ghost.ParentDestroyed = isParentDestroyed;

			ghost.Local         = snapshotLocal;
			ghost.Self          = self;
			ghost.Remote        = snapshotRemote;
			ghost.IsDataIgnored = false;

			ghosts[snapshotLocal.Id] = ghost;
			selfToSnapshot[self]     = snapshotLocal;
		}

		public void FinalizeEntities()
		{
			foreach (var ghost in ghosts)
			{
				if (!ghost.IsInitialized)
					continue;

				entities.Add(ghost.Self.Id);
			}
		}

		public void SetArchetypeSystems(uint snapshotArchetype, ReadOnlySpan<uint> systemIds)
		{
			archetypeToSystems[snapshotArchetype] = systemIds.ToArray();
			if (!archetypes.Contains(snapshotArchetype))
				archetypes.Add(snapshotArchetype);
		}

		public void AssignArchetype(uint entity, uint snapshotArchetype)
		{
			GetRefGhost(entity).Archetype = snapshotArchetype;
		}
	}
}
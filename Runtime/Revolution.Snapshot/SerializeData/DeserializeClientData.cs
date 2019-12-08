using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{
	public struct DeserializeClientData : IDisposable
	{
		public Entity Client;
		public uint   Tick;

		public NativeList<uint>   GhostIds;
		public NativeList<Entity> Entities;

		public NativeHashMap<uint, Entity> GhostToEntityMap;
		public NativeList<uint>            KnownArchetypes;

		public NetworkCompressionModel NetworkCompressionModel;

		private const int FirstCapacity = 256;

		public DeserializeClientData(Allocator allocator)
		{
			if (allocator != Allocator.Persistent)
				throw new NotImplementedException();

			GhostIds                = new NativeList<uint>(FirstCapacity, Allocator.Persistent);
			Entities                = new NativeList<Entity>(FirstCapacity, Allocator.Persistent);
			GhostToEntityMap        = new NativeHashMap<uint, Entity>(FirstCapacity, Allocator.Persistent);
			NetworkCompressionModel = new NetworkCompressionModel(Allocator.Persistent);
			KnownArchetypes         = new NativeList<uint>(Allocator.Persistent);

			Client = default;
			Tick   = 0;
		}

		public void Dispose()
		{
			GhostIds.Dispose();
			Entities.Dispose();
			GhostToEntityMap.Dispose();
			NetworkCompressionModel.Dispose();
			KnownArchetypes.Dispose();
		}

		/// <summary>
		///     Set the entity for an existing ghost.
		/// </summary>
		/// <remarks>
		///     This method is useful for predictive spawning (spawning an entity client-side that is then attached to a
		///     ghost)
		/// </remarks>
		/// <param name="index">The ghost at index</param>
		/// <param name="entity">The entity replacement</param>
		public void SetEntityAtIndex(int index, Entity entity)
		{
			Entities[index]                   = entity;
			GhostToEntityMap[GhostIds[index]] = entity;
		}

		/// <summary>
		///     Set the entity for an existing ghost.
		/// </summary>
		/// <remarks>
		///     This method is useful for predictive spawning (spawning an entity client-side that is then attached to a
		///     ghost)
		/// </remarks>
		/// <param name="ghostId">The ghost id</param>
		/// <param name="entity">The entity replacement</param>
		/// <exception cref="KeyNotFoundException">No ghost found with <see cref="ghostId" />></exception>
		public void SetEntityForGhost(uint ghostId, Entity entity)
		{
			for (int i = 0, length = GhostIds.Length; i < length; i++)
				if (GhostIds[i] == ghostId)
				{
					Entities[i]               = entity;
					GhostToEntityMap[ghostId] = entity;
					return;
				}

			throw new KeyNotFoundException($"No ghost found with id={ghostId}");
		}
	}
}
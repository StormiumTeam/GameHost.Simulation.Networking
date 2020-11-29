using System;
using System.Collections.Generic;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Components
{
	public enum EntitySnapshotPriority
	{
		/// <summary>
		///     This entity don't exist.
		/// </summary>
		DontExist = 0,

		/// <summary>
		///     This entity has no priority at all.
		/// </summary>
		NoPriority = 1,

		/// <summary>
		///     The archetype of this entity must be sent.
		/// </summary>
		Archetype = 1,

		/// <summary>
		///     The components data of this entity must be sent.
		/// </summary>
		Component = 2,

		/// <summary>
		///     This entity data must be sent.
		///     (If the serializer has a bytes limit, it will force this entity to be sent)
		/// </summary>
		SendAtAllCost = 3
	}

	public class EntityGroup : Dictionary<GameEntity, EntitySnapshotPriority>
	{
		public EntityGroup()
		{
		}

		public EntityGroup(EntityGroup other) : base(other)
		{
		}

		public void Set(EntitySnapshotPriority[] array)
		{
			foreach (var (entity, priority) in this)
			{
				ref var currPriority = ref array[entity.Id];
				currPriority = (EntitySnapshotPriority) Math.Max((int) currPriority, (int) priority);
			}
		}

		// kinda slow
		public void IntersectWith(List<GameEntity> entityList, List<EntitySnapshotPriority> priorityList, EntityGroup other)
		{
			foreach (var (entity, priority) in other)
			{
				if (!TryGetValue(entity, out var thisPriority))
					continue;

				entityList.Add(entity);
				priorityList.Add((EntitySnapshotPriority) Math.Max((int) thisPriority, (int) priority));
			}
		}
	}
}
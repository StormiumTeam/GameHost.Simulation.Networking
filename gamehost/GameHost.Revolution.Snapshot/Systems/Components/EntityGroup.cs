using System;
using Collections.Pooled;
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

	public class EntityGroup : PooledList<GameEntity>
	{
		public EntitySnapshotPriority[] Values = Array.Empty<EntitySnapshotPriority>();
		
		public EntityGroup()
		{
		}

		public EntitySnapshotPriority this[GameEntity entity]
		{
			get => Values[entity.Id];
			set
			{
				if (entity.Id + 1 >= Values.Length)
				{
					Array.Resize(ref Values, (int) entity.Id * 2 + 1);
				}

				if (value != default && Values[entity.Id] == default)
				{
					Values[entity.Id] = value;
					Add(entity);
				}
			}
		}

		public void FullClear()
		{
			Clear();
			Array.Fill(Values, default);
		}
	}
}
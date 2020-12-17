using System;
using System.Collections;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;

namespace GameHost.Revolution.Snapshot.Utilities
{
	public class MergeGroupCollection : IEnumerable<MergeGroup>
	{
		private readonly Dictionary<Entity, MergeGroup> entityToGroup = new();
		private readonly List<MergeGroup>               groups        = new();

		public List<MergeGroup>.Enumerator GetEnumerator()
		{
			return groups.GetEnumerator();
		}

		IEnumerator<MergeGroup> IEnumerable<MergeGroup>.GetEnumerator()
		{
			return groups.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public MergeGroup CreateGroup()
		{
			var group = new MergeGroup();
			groups.Add(group);

			return group;
		}

		public bool SetToGroup(in Entity client, in MergeGroup? group)
		{
			if (entityToGroup.TryGetValue(client, out var oldGroup))
			{
				if (group == oldGroup)
					return false;

				if (oldGroup.Entities.Remove(client))
					oldGroup.EntitiesList.Remove(client);

				if (oldGroup.Entities.Count == 0)
					oldGroup.Storage.Dispose();
			}

			if (group == null) return entityToGroup.Remove(client);

			if (!group.Storage.IsAlive)
				group.Storage = client.World.CreateEntity();

			entityToGroup[client] = group;
			if (group.Entities.Add(client))
			{
				group.EntitiesList.Add(client);
				return true;
			}

			return false;
		}

		public bool TryGetGroup(Entity client, out MergeGroup group)
		{
			return entityToGroup.TryGetValue(client, out group!);
		}

		public MergeGroup GetGroup(Entity client) => entityToGroup[client];
	}

	public class MergeGroup : IEnumerable<Entity>
	{
		internal HashSet<Entity>    Entities     = new();
		internal PooledList<Entity> EntitiesList = new();

		public Span<Entity> ClientSpan => EntitiesList.Span;

		public Entity Storage { get; internal set; }

		public IEnumerator<Entity> GetEnumerator()
		{
			return Entities.GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return Entities.GetEnumerator();
		}
	}
}
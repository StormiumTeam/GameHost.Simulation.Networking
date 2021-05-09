using System;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public class SnapshotFrame
	{
		private PooledList<GameEntity>         entities   = new();
		private PooledDictionary<uint, Entity> systemData = new();

		private World world;

		public SnapshotFrame(World world)
		{
			this.world = world;
		}

		/// <summary>
		/// Use for pooling. Clear the written and read data
		/// </summary>
		public void Clear()
		{
			entities.Clear();
			systemData.Clear();
		}

		private SnapshotFrameWriter? cachedWriter;

		public SnapshotFrameWriter Write(in SnapshotBuilder builder)
		{
			return cachedWriter ?? new(world, entities, systemData);
		}

		public Span<GameEntity> Entities => entities.Span;
	}

	public class SnapshotFrameWriter
	{
		private readonly World world;

		private readonly PooledList<GameEntity>         entities;
		private readonly PooledDictionary<uint, Entity> systemData;

		public Span<GameEntity> Entities => entities.Span;
		
		internal SnapshotFrameWriter(World world, PooledList<GameEntity> entities, PooledDictionary<uint, Entity> systemData)
		{
			this.world = world;

			this.entities   = entities;
			this.systemData = systemData;
		}

		public void AddEntity(GameEntity entity)
		{
			entities.Add(entity);
		}

		public void AddSystem(uint handle, ISnapshotSystem system)
		{
			var ent = world.CreateEntity();
			ent.Set(system);

			systemData[handle] = ent;
		}

		public void Execute()
		{
			foreach (var (handle, entity) in systemData)
			{
				var system = entity.Get<ISnapshotSystem>();
				system.PrepareWrite(this);
			}
		}
	}
}
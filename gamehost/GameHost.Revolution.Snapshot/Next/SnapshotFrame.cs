using System;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Revolution.NetCode.Next.Batch;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using StormiumTeam.GameBase.Utility.Misc.EntitySystem;

namespace GameHost.Revolution.NetCode.Next
{
	public class SnapshotFrame
	{
		private PooledList<GameEntity>         entities   = new();
		private PooledDictionary<uint, Entity> systemData = new();

		private ColumnView<GameEntity>   entityView = new();
		private Column<GameEntity, uint> entityArchetypes;

		private World world;

		public SnapshotFrame(World world)
		{
			this.world = world;

			entityArchetypes = new(entityView);
		}

		/// <summary>
		/// Use for pooling. Clear the written and read data
		/// </summary>
		public void Clear()
		{
			entityView.Clear();
			entityArchetypes.Clear();
			
			entities.Clear();
			systemData.Clear();

			cachedWriter?.Reset();
		}

		private SnapshotFrameWriter? cachedWriter;

		public SnapshotFrameWriter GetWriter()
		{
			return cachedWriter ??= new(world, new()
			{
				EntityView = entityView,
				EntityToArchetype = entityArchetypes,
				
				SystemData = systemData,

				Root = Root,
				
				EntitiesToFill = entities
			});
		}

		public SnapshotRoot Root
		{
			get
			{
				var roots = world.Get<SnapshotRoot>();
				if (roots.IsEmpty)
				{
					var root = new SnapshotRoot();
					world.CreateEntity().Set(root);
					return root;
				}

				return roots[0];
			}
		}

		public Span<GameEntity> Entities
		{
			get
			{
				if (cachedWriter!.HasBuilt == false)
					throw new InvalidOperationException("The Writer hasn't built entities yet");

				return entities.Span;
			}
		}

		public Span<GameEntity> EntityView => entityView;

		public Span<uint> EntityToArchetype => entityArchetypes.Array;
		
		public IReadOnlyDictionary<uint, Entity> SystemStorage     => systemData;

		private SystemBatch? cachedSystemBatch;

		public IBatch GetSystemBatch(Entity forClient = default, SnapshotFrame? baseline = null)
		{
			cachedSystemBatch          ??= new();
			cachedSystemBatch.Target   =   this;
			cachedSystemBatch.Client   =   forClient;
			cachedSystemBatch.Baseline =   baseline;

			return cachedSystemBatch;
		}

		public void CompleteSystemBatch(BitBuffer output)
		{
			cachedSystemBatch!.Complete(output);
		}
	}

	public class WrittenClientData
	{
		private Dictionary<Entity, PooledList<byte>> clientDataMap = new();

		public PooledList<byte> GetClientData(Entity client)
		{
			if (clientDataMap.TryGetValue(client, out var bytes))
				return bytes;

			clientDataMap[client] = bytes = new();
			return bytes;
		}
	}
}
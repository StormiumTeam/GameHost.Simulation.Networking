using System;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public class SnapshotFrameWriter
	{
		private readonly World world;

		public struct FrameInput
		{
			public ColumnView<GameEntity>   EntityView;
			public Column<GameEntity, uint> EntityToArchetype;

			public PooledDictionary<uint, Entity> SystemData;

			public SnapshotRoot Root;

			public PooledList<GameEntity> EntitiesToFill;
		}

		public readonly FrameInput Input;
		
		public Span<GameEntity> Entities
		{
			get
			{
				if (!HasBuilt)
					throw new NullReferenceException("Call PrepareSystems() or Build()");

				return Input.EntitiesToFill.Span;
			}
		}

		public bool HasBuilt { get; private set; }

		private struct TemporaryVariables
		{
			public PooledList<uint> EntityArchetypeSystems;
		}

		private TemporaryVariables temporaryVariables = new()
		{
			EntityArchetypeSystems = new()
		};

		internal SnapshotFrameWriter(World world, FrameInput input)
		{
			this.world = world;

			Input = input;
		}

		/// <summary>
		/// Add an entity to the writer
		/// </summary>
		/// <param name="entity">The entity</param>
		/// <param name="archetype">The optional archetype of the entity (if the writer is used as a serializer you can keep it to default)</param>
		/// <remarks>
		///	The archetype value will be modified in <see cref="BuildInWritingContext"/>
		/// </remarks>
		public void AddEntity(GameEntity entity, uint? archetype = null)
		{
			Input.EntityView.GetValue(entity.Id)        = entity;
			Input.EntityToArchetype.GetValue(entity.Id) = archetype ?? Input.Root.Archetypes[0];
		}

		public void AddSystem(uint handle, ISnapshotSystem system)
		{
			if (Input.SystemData.TryGetValue(handle, out var ent))
				return;

			ent = world.CreateEntity();
			ent.Set(system);
			ent.Set(new WrittenClientData());

			Input.SystemData[handle] = ent;
		}

		public void BuildInReadingContext()
		{
			void prepareEntities()
			{
				Span<GameEntity> entityView = Input.EntityView.Array;
				for (var i = 0; i < entityView.Length; i++)
					if (entityView[i] != default)
						Input.EntitiesToFill.Add(entityView[i]);
			}
			
			HasBuilt = true;
			
			prepareEntities();
		}

		public void BuildInWritingContext(GameWorld? gameWorld)
		{
			void prepareEntities(GameWorld gameWorld)
			{
				Span<GameEntity> entityView = Input.EntityView.Array;
				for (var i = 0; i < entityView.Length; i++)
				{
					var gameEntity = entityView[i];
					if (gameEntity != default && gameWorld.Exists(gameEntity))
					{
						Input.EntitiesToFill.Add(gameEntity);

						var systemList = temporaryVariables.EntityArchetypeSystems;
						{
							systemList.Clear();
						}

						foreach (var (idx, entity) in Input.SystemData)
						{
							var system = entity.Get<ISnapshotSystem>();
							/*if (system is ISnapshotSystemSupportEntity support
							    && support.IsEntityValid(gameWorld, gameEntity))
							{
								systemList.Add(idx);
							}*/
						}

						Input.EntityToArchetype.GetValue(gameEntity.Id) = Input.Root.GetOrCreateArchetype(systemList.Span);
					}
				}
			}

			void prepareSystems()
			{
				foreach (var (_, entity) in Input.SystemData)
				{
					var system = entity.Get<ISnapshotSystem>();
					system.PrepareWrite(this, entity);
				}
			}

			if (HasBuilt)
			{
				throw new InvalidOperationException("Already built.\nIf you want to rebuild again with new data call Reset()");
			}
			
			HasBuilt = true;

			if (gameWorld != null)
			{
				prepareEntities(gameWorld);
			}
			else if (!Entities.IsEmpty)
			{
				throw new InvalidOperationException("Parameter gameWorld was null, but entities were added to this frame");
			}

			prepareSystems();
		}

		public void Reset()
		{
			HasBuilt = false;
		}
	}
}
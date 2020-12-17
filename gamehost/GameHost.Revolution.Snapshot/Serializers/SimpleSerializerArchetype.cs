using System;
using System.Collections.Generic;
using Collections.Pooled;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;
using StormiumTeam.GameBase.Utility.Misc;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public class SimpleSerializerArchetype : ISerializerArchetype
	{
		private readonly ComponentType[] entityComponentBackend;
		private readonly ComponentType[] excludedComponentBackend;
		private readonly GameWorld       gameWorld;

		public readonly ISerializer              Serializer;
		public readonly SnapshotSerializerSystem System;

		public SimpleSerializerArchetype(ISerializer   serializer,    GameWorld       gameWorld,
		                                 ComponentType coreComponent, ComponentType[] entityComponents, ComponentType[] excludedComponents)
		{
			this.gameWorld           = gameWorld;
			CoreComponentBackend     = coreComponent;
			entityComponentBackend   = entityComponents;
			excludedComponentBackend = excludedComponents;

			Serializer = serializer;
			System     = serializer.System;
		}

		public ComponentType CoreComponentBackend { get; }

		public Span<ComponentType> ExcludedComponents => excludedComponentBackend;

		public Span<ComponentType> EntityComponents => entityComponentBackend;

		public bool IsArchetypeValid(in EntityArchetype archetype)
		{
			var archetypeBoard = gameWorld.Boards.Archetype;
			var componentTypes = archetypeBoard.GetComponentTypes(archetype.Id);
			if (entityComponentBackend.Length > componentTypes.Length)
				return false;

			var ownLength = entityComponentBackend.Length;
			var match     = 0;
			for (var i = 0; i < componentTypes.Length && ownLength > match; i++)
			{
				// If the chunk excluded us, there is no reason to continue...
				if (Array.IndexOf(excludedComponentBackend, componentTypes[i]) >= 0)
					return false;

				if (Array.IndexOf(entityComponentBackend, new ComponentType(componentTypes[i])) >= 0)
					match++;
			}
			
			return match == ownLength;
		}

		public void OnDeserializerArchetypeUpdate(Span<GameEntity> entities, Span<SnapshotEntityArchetype> archetypes, 
		                                          Dictionary<uint, uint[]> archetypeToSystems,
		                                          Span<bool> authority)
		{
			for (int ent = 0, length = entities.Length; ent < length; ent++)
			{
				var archetype = archetypes[ent];
				var models    = archetypeToSystems[archetype.Id];
				var hasModel  = false;
				
				// Search if this entity has our system from the model list
				foreach (var model in models)
				{
					// Bingo! This entity got our system
					if (model == System.Id)
					{
						// If this entity don't have the snapshot buffer yet, add it.
						if (!gameWorld.HasComponent(entities[ent].Handle, CoreComponentBackend))
						{
							gameWorld.AddComponent(entities[ent].Handle, CoreComponentBackend);
							// Don't add components if we don't have the authority to do so.
							if (!authority[(int) entities[ent].Id])
								gameWorld.AddMultipleComponent(entities[ent].Handle, EntityComponents);
						}

						hasModel = true;
						break;
					}
				}

				// If we have a model, or if the parent instigator has an authority, continue...
				if (hasModel)
					continue;
				if (!gameWorld.HasComponent(entities[ent].Handle, CoreComponentBackend))
					continue;

				// If the entity had the snapshot (so the model) before, but now it don't have it anymore, remove the snapshot and components
				if (!authority[(int) entities[ent].Id])
				{
					using var componentsToRemove = new PooledList<ComponentType>(EntityComponents.Length);
					componentsToRemove.AddRange(EntityComponents);
					foreach (var model in models)
					{
						var otherSerializer = Serializer.Instigator.Serializers[model];
						if (otherSerializer.SerializerArchetype == null)
							continue;

						var otherComponents = otherSerializer.SerializerArchetype.EntityComponents;
						for (var i = 0; i < componentsToRemove.Count; i++)
						{
							if (!otherComponents.Contains(componentsToRemove[i]))
								continue;

							// If that system got the same component as us, remove if from the remove list.
							componentsToRemove.RemoveAt(i--);
						}
					}

					for (var i = 0; i < componentsToRemove.Count; i++)
						gameWorld.RemoveComponent(entities[ent].Handle, componentsToRemove[i]);
				}

				Console.WriteLine($"{gameWorld.Boards.ComponentType.NameColumns[(int) CoreComponentBackend.Id]} has been removed from {entities[ent]}");
				gameWorld.RemoveComponent(entities[ent].Handle, CoreComponentBackend);
			}
		}
	}
}
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Revolution
{
	/// <summary>
	/// Base class for doing entity standard snapshot operations
	/// </summary>
	/// <typeparam name="TSerializer"></typeparam>
	/// <typeparam name="TComponent"></typeparam>
	/// <typeparam name="TSharedData"></typeparam>
	public abstract class EntitySerializerComponent<TSerializer, TComponent, TSharedData> : JobComponentSystem, IEntityComponents, ISystemDelegateForSnapshot, IDynamicSnapshotSystem
		where TSerializer : EntitySerializerComponent<TSerializer, TComponent, TSharedData>
		where TComponent : struct, IComponentData
		where TSharedData : struct
	{
		public abstract NativeArray<ComponentType> EntityComponents { get; }
		public abstract ComponentType              ExcludeComponent { get; }

		public const uint SnapshotHistorySize = 16;

		
		
		protected override void OnCreate()
		{
			base.OnCreate();

			World.GetOrCreateSystem<SnapshotManager>().RegisterSystem(this);
			
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			m_BufferSafetyHandle = AtomicSafetyHandle.Create();
#endif
			
			GetDelegates(out m_SerializeDelegate, out m_DeserializeDelegate);
		}
		
		protected override void OnDestroy()
		{
			base.OnDestroy();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.Release(m_BufferSafetyHandle);
#endif
		}

		public ref SharedSystemChunk GetSharedChunk()
		{
			return ref GetSerializerChunkData();
		}

		public ref SharedSystemGhost GetSharedGhost()
		{
			return ref GetDeserializerGhostData();
		}

		public bool IsChunkValid(ArchetypeChunk chunk)
		{
			var ownComponents  = EntityComponents;
			var componentTypes = chunk.Archetype.GetComponentTypes();
			if (componentTypes.Length < ownComponents.Length)
				return false;

			var ownLength = ownComponents.Length;
			var match     = 0;
			for (var i = 0; i < componentTypes.Length; i++)
			{
				// If the chunk excluded us, there is no reason to continue...
				if (componentTypes[i].TypeIndex == ExcludeComponent.TypeIndex)
					return false;

				for (var j = 0; j < ownLength; j++)
				{
					if (componentTypes[i].TypeIndex == ownComponents[j].TypeIndex)
						match++;
				}
			}

			return match == ownLength;
		}

		public void OnDeserializerArchetypeUpdate(NativeArray<Entity> entities, NativeArray<uint> archetypes, Dictionary<uint, NativeArray<uint>> archetypeToSystems)
		{
			var systemId        = World.GetOrCreateSystem<SnapshotManager>().GetSystemId(this);
			var snapshotManager = World.GetOrCreateSystem<SnapshotManager>();
			for (int ent = 0, length = entities.Length; ent < length; ent++)
			{
				var archetype = archetypes[ent];
				var models    = archetypeToSystems[archetype];
				var hasModel  = false;

				// Search if this entity has our system from the model list
				foreach (var model in models)
				{
					// Bingo! This entity got our system
					if (model == systemId)
					{
						hasModel = true;
						break;
					}
				}

				if (hasModel)
					continue;

				if (!EntityManager.HasComponent<TComponent>(entities[ent]))
					continue;


				if (!EntityComponents.Contains(ComponentType.ReadWrite<TComponent>()))
					throw new InvalidOperationException();

				// If the entity had the snapshot (so the model) before, but now it don't have it anymore, remove the snapshot and components
				var componentsToRemove = new NativeList<ComponentType>(EntityComponents.Length, Allocator.Temp);
				componentsToRemove.AddRange(EntityComponents);
				foreach (var model in models)
				{
					var system = snapshotManager.GetSystem(model);

					// First, check if another system got the same components as us.
					if (system is IEntityComponents systemWithInterface)
					{
						var systemEntityComponents = systemWithInterface.EntityComponents;
						for (var i = 0; i < componentsToRemove.Length; i++)
						{
							if (!systemEntityComponents.Contains(componentsToRemove[i]))
								continue;

							// If that system got the same component as us, remove if from the remove list.
							componentsToRemove.RemoveAtSwapBack(i);
							i--;
						}
					}
				}

				foreach (var type in componentsToRemove)
					EntityManager.RemoveComponent(entities[ent], type);
			}
		}

		protected abstract void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize);

		public FunctionPointer<OnSerializeSnapshot> SerializeDelegate => m_SerializeDelegate.Get();
		public FunctionPointer<OnDeserializeSnapshot> DeserializeDelegate => m_DeserializeDelegate.Get();
		
		private BurstDelegate<OnSerializeSnapshot>   m_SerializeDelegate;
		private BurstDelegate<OnDeserializeSnapshot> m_DeserializeDelegate;

		public abstract void OnBeginSerialize(Entity entity);

		public abstract void OnBeginDeserialize(Entity entity);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
		private AtomicSafetyHandle m_BufferSafetyHandle;

		protected AtomicSafetyHandle SafetyHandle => m_BufferSafetyHandle;
#endif

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return inputDeps;
		}

		class SharedKey__
		{
		}

		class ChunkKey__
		{
		}

		class GhostKey__
		{
		}

		protected static ref TSharedData GetShared()
		{
			return ref SharedStatic<TSharedData>.GetOrCreate<SharedKey__>().Data;
		}

		protected static ref SharedSystemChunk GetSerializerChunkData()
		{
			return ref SharedStatic<SharedSystemChunk>.GetOrCreate<ChunkKey__>().Data;
		}

		protected static ref SharedSystemGhost GetDeserializerGhostData()
		{
			return ref SharedStatic<SharedSystemGhost>.GetOrCreate<GhostKey__>().Data;
		}
	}
}
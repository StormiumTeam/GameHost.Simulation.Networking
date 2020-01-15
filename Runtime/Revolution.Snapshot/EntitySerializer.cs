using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;

namespace Revolution
{
	/// <summary>
	///     Base class for doing entity standard snapshot operations
	/// </summary>
	/// <typeparam name="TSerializer"></typeparam>
	/// <typeparam name="TSnapshot"></typeparam>
	/// <typeparam name="TSharedData"></typeparam>
	[UpdateInGroup(typeof(SnapshotWithDelegateSystemGroup))]
	public abstract class EntitySerializer<TSerializer, TSnapshot, TSharedData> : JobComponentSystem, IEntityComponents, ISystemDelegateForSnapshot, IDynamicSnapshotSystem
		where TSerializer : EntitySerializer<TSerializer, TSnapshot, TSharedData>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, IBufferElementData
		where TSharedData : struct
	{
		public override string ToString()
		{
			return $"EntitySerializer<{typeof(TSerializer)}, {typeof(TSnapshot)}, {typeof(TSharedData)}>";
		}

		public const uint                                 SnapshotHistorySize = SnapshotDataExtensions.SnapshotHistorySize;
		private      BurstDelegate<OnDeserializeSnapshot> m_DeserializeDelegate;

		private         BurstDelegate<OnSerializeSnapshot> m_SerializeDelegate;
		public abstract ComponentType                      ExcludeComponent { get; }

		private NativeString512 m_NativeName;
		public NativeString512 NativeName => m_NativeName;

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
					if (componentTypes[i].TypeIndex == ownComponents[j].TypeIndex)
						match++;
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
					// Bingo! This entity got our system
					if (model == systemId)
					{
						// If this entity don't have the snapshot buffer yet, add it.
						if (!EntityManager.HasComponent<TSnapshot>(entities[ent]))
						{
							var snapshotBuffer = EntityManager.AddBuffer<TSnapshot>(entities[ent]);
							snapshotBuffer.ResizeUninitialized((int) SnapshotHistorySize);
							snapshotBuffer.Clear();
						}

						hasModel = true;
						break;
					}

				if (hasModel)
					continue;
				if (!EntityManager.HasComponent<TSnapshot>(entities[ent]))
					continue;

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

				for (var i = 0; i < componentsToRemove.Length; i++)
					EntityManager.RemoveComponent(entities[ent], componentsToRemove[i]);

				EntityManager.RemoveComponent<TSnapshot>(entities[ent]);
			}
		}

		public abstract NativeArray<ComponentType> EntityComponents { get; }

		public FunctionPointer<OnSerializeSnapshot>   SerializeDelegate   => m_SerializeDelegate.Get();
		public FunctionPointer<OnDeserializeSnapshot> DeserializeDelegate => m_DeserializeDelegate.Get();

		public abstract void OnBeginSerialize(Entity entity);

		public abstract void OnBeginDeserialize(Entity entity);

		protected virtual void SetSystemGroup()
		{
			var delegateGroup = World.GetOrCreateSystem<SnapshotWithDelegateSystemGroup>();
			if (!delegateGroup.Systems.Contains(this))
				delegateGroup.AddSystemToUpdateList(this);
		}


		protected override void OnCreate()
		{
			base.OnCreate();

			World.GetOrCreateSystem<SnapshotManager>().RegisterSystem(this);
			m_NativeName = ToString();

			SetSystemGroup();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyHandle = AtomicSafetyHandle.Create();
#endif

			GetDelegates(out m_SerializeDelegate, out m_DeserializeDelegate);
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			AtomicSafetyHandle.Release(SafetyHandle);
#endif
		}

		protected abstract void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize);

		public void SetEmptySafetyHandle(ref BufferFromEntity<TSnapshot> bfe)
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyUtility.Replace(ref bfe, SafetyHandle);
#endif
		}

		public void SetEmptySafetyHandle<TComponent>(ref ArchetypeChunkComponentType<TComponent> comp) where TComponent : struct, IComponentData
		{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyUtility.Replace(ref comp, SafetyHandle);
#endif
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return inputDeps;
		}

		protected static ref TSharedData GetShared()
		{
			return ref SharedStatic<TSharedData>.GetOrCreate<SharedKey>().Data;
		}

		protected static ref SharedSystemChunk GetSerializerChunkData()
		{
			return ref SharedStatic<SharedSystemChunk>.GetOrCreate<ChunkKey>().Data;
		}

		protected static ref SharedSystemGhost GetDeserializerGhostData()
		{
			return ref SharedStatic<SharedSystemGhost>.GetOrCreate<GhostKey>().Data;
		}

		private class SharedKey
		{
		}

		private class ChunkKey
		{
		}

		private class GhostKey
		{
		}

#if ENABLE_UNITY_COLLECTIONS_CHECKS

		protected AtomicSafetyHandle SafetyHandle { get; private set; }
#endif
	}
}
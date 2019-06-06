using Unity.NetCode;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace DefaultNamespace
{
#if UNITY_NETCODE_MODIFIED
	public abstract class AddComponentSerializer<TComponent, TSnapshot> : AddSerializer<ComponentSerializer<TComponent, TSnapshot>, TSnapshot>
		where TComponent : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent>
	{
		public override ComponentTypes GetComponents()
		{
			return new ComponentTypes(typeof(TComponent), typeof(TSnapshot));
		}
	}

	public struct ComponentSerializer<TComponent, TSnapshot> : IGhostSerializer<TSnapshot>
		where TComponent : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent>
	{
		public GhostSerializerHeader Header { get; }

		[NativeDisableContainerSafetyRestriction]
		public ArchetypeChunkComponentType<TComponent> ghostTestType;

		public void SetupHeader(ComponentSystemBase system, ref GhostSerializerHeader header)
		{
			var serializerSystem = system.World.GetExistingSystem<AddComponentSerializer<TComponent, TSnapshot>>();

			header.WantsPredictionDelta = serializerSystem.WantsPredictionDelta;
			header.WantsSingleHistory = serializerSystem.WantsSingleHistory;
			header.Importance = serializerSystem.Importance;
		}

		public void BeginSerialize(ComponentSystemBase system)
		{
			ghostTestType = system.GetArchetypeChunkComponentType<TComponent>();

			Type = typeof(TComponent);
		}

		public void BeginDeserialize(JobComponentSystem system)
		{
			var s = system.World.GetExistingSystem<AddComponentSerializer<TComponent, TSnapshot>.SystemGhostSerializer>();
			
			NewGhosts = s.NewGhosts;
			NewGhostIds = s.NewGhostIds;
		}

		public void Spawn(int ghostId, TSnapshot data)
		{
			NewGhosts.Add(data);
			NewGhostIds.Add(ghostId);
		}

		public ComponentType            Type;
		public ResizableList<TSnapshot> NewGhosts;
		public ResizableList<int>       NewGhostIds;

		public bool CanSerialize(EntityArchetype arch)
		{
			var components = arch.GetComponentTypes();
			for (var i = 0; i != components.Length; i++)
			{
				if (components[i] == Type)
					return true;
			}

			return false;
		}

		public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref TSnapshot snapshot)
		{
			var components = chunk.GetNativeArray(ghostTestType);

			snapshot.Set(components[ent]);
		}
	}
#endif
}
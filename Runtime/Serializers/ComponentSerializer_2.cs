using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace DefaultNamespace
{
	public interface ISnapshotFromComponent<TSnapshotData, in TComponent1, in TComponent2> : ISnapshotData<TSnapshotData>
		where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
	{
		void Set(TComponent1 component1, TComponent2 component2);
	}

	public abstract class AddComponentSerializer<TComponent1, TComponent2, TSnapshot> : AddSerializer<ComponentSerializer<TComponent1, TComponent2, TSnapshot>, TSnapshot>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent1, TComponent2>
	{
		public override ComponentTypes GetComponents()
		{
			return new ComponentTypes(typeof(TComponent1), typeof(TComponent2), typeof(TSnapshot));
		}
	}

	public struct ComponentSerializer<TComponent1, TComponent2, TSnapshot> : IGhostSerializer<TSnapshot>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent1, TComponent2>
	{
		public GhostSerializerHeader Header { get; }


		public ArchetypeChunkComponentType<TComponent1> component1Type;
		public ArchetypeChunkComponentType<TComponent2> component2Type;

		public void SetupHeader(ComponentSystemBase system, ref GhostSerializerHeader header)
		{
			var serializerSystem = system.World.GetExistingSystem<AddComponentSerializer<TComponent1, TComponent2, TSnapshot>>();

			header.WantsPredictionDelta = serializerSystem.WantsPredictionDelta;
			header.WantsSingleHistory   = serializerSystem.WantsSingleHistory;
			header.Importance           = serializerSystem.Importance;
		}

		public void BeginSerialize(ComponentSystemBase system)
		{
			component1Type = system.GetArchetypeChunkComponentType<TComponent1>();
			component2Type = system.GetArchetypeChunkComponentType<TComponent2>();

			Type1 = typeof(TComponent1);
			Type2 = typeof(TComponent2);
		}

		public void BeginDeserialize(JobComponentSystem system)
		{
			var s = system.World.GetExistingSystem<AddComponentSerializer<TComponent1, TComponent2, TSnapshot>.SystemGhostSerializer>();

			NewGhosts   = s.NewGhosts;
			NewGhostIds = s.NewGhostIds;
		}

		public void Spawn(int ghostId, TSnapshot data)
		{
			NewGhosts.Add(data);
			NewGhostIds.Add(ghostId);
		}

		public ComponentType Type1;
		public ComponentType Type2;

		public ResizableList<TSnapshot> NewGhosts;
		public ResizableList<int>       NewGhostIds;

		public bool CanSerialize(EntityArchetype arch)
		{
			var matches    = 0;
			var components = arch.GetComponentTypes();
			for (var i = 0; i != components.Length; i++)
			{
				if (components[i] == Type1)
					matches++;
				if (components[i] == Type2)
					matches++;
			}

			return matches >= 2;
		}

		public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref TSnapshot snapshot)
		{
			var component1 = chunk.GetNativeArray(component1Type);
			var component2 = chunk.GetNativeArray(component2Type);

			snapshot.Set(component1[ent], component2[ent]);
		}
	}
}
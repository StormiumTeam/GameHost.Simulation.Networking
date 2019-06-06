using Unity.NetCode;
using Unity.Entities;
using UnityEngine;

namespace DefaultNamespace
{
#if UNITY_NETCODE_MODIFIED
	public interface ISnapshotFromComponent<TSnapshotData, in TComponent1, in TComponent2, in TComponent3, in TComponent4> : ISnapshotData<TSnapshotData>
		where TSnapshotData : unmanaged, ISnapshotData<TSnapshotData>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
		where TComponent3 : struct, IComponentData
		where TComponent4 : struct, IComponentData
	{
		void Set(TComponent1 component1, TComponent2 component2, TComponent3 component3, TComponent4 component4);
	}

	public abstract class AddComponentSerializer<TComponent1, TComponent2, TComponent3, TComponent4, TSnapshot> : AddSerializer<ComponentSerializer<TComponent1, TComponent2, TComponent3, TComponent4, TSnapshot>, TSnapshot>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
		where TComponent3 : struct, IComponentData
		where TComponent4 : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent1, TComponent2, TComponent3, TComponent4>
	{
		public override ComponentTypes GetComponents()
		{
			return new ComponentTypes(typeof(TComponent1), typeof(TComponent2), typeof(TComponent3), typeof(TSnapshot));
		}
	}

	public struct ComponentSerializer<TComponent1, TComponent2, TComponent3, TComponent4, TSnapshot> : IGhostSerializer<TSnapshot>
		where TComponent1 : struct, IComponentData
		where TComponent2 : struct, IComponentData
		where TComponent3 : struct, IComponentData
		where TComponent4 : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent1, TComponent2, TComponent3, TComponent4>
	{
		public GhostSerializerHeader Header { get; }


		public ArchetypeChunkComponentType<TComponent1> component1Type;
		public ArchetypeChunkComponentType<TComponent2> component2Type;
		public ArchetypeChunkComponentType<TComponent3> component3Type;
		public ArchetypeChunkComponentType<TComponent4> component4Type;

		public void SetupHeader(ComponentSystemBase system, ref GhostSerializerHeader header)
		{
			var serializerSystem = system.World.GetExistingSystem<AddComponentSerializer<TComponent1, TComponent2, TComponent3, TComponent4, TSnapshot>>();

			header.WantsPredictionDelta = serializerSystem.WantsPredictionDelta;
			header.WantsSingleHistory = serializerSystem.WantsSingleHistory;
			header.Importance = serializerSystem.Importance;
		}

		public void BeginSerialize(ComponentSystemBase system)
		{
			component1Type = system.GetArchetypeChunkComponentType<TComponent1>();
			component2Type = system.GetArchetypeChunkComponentType<TComponent2>();
			component3Type = system.GetArchetypeChunkComponentType<TComponent3>();
			component4Type = system.GetArchetypeChunkComponentType<TComponent4>();

			Type1 = typeof(TComponent1);
			Type2 = typeof(TComponent2);
			Type3 = typeof(TComponent3);
			Type4 = typeof(TComponent4);
		}

		public void BeginDeserialize(JobComponentSystem system)
		{
			var s = system.World.GetExistingSystem<AddComponentSerializer<TComponent1, TComponent2, TComponent3, TComponent4, TSnapshot>.SystemGhostSerializer>();

			NewGhosts = s.NewGhosts;
			NewGhostIds = s.NewGhostIds;
		}

		public void Spawn(int ghostId, TSnapshot data)
		{
			NewGhosts.Add(data);
			NewGhostIds.Add(ghostId);
		}

		public ComponentType Type1;
		public ComponentType Type2;
		public ComponentType Type3;
		public ComponentType Type4;

		public ResizableList<TSnapshot> NewGhosts;
		public ResizableList<int>       NewGhostIds;

		public bool CanSerialize(EntityArchetype arch)
		{
			var matches = 0;
			var components = arch.GetComponentTypes();
			for (var i = 0; i != components.Length; i++)
			{
				if (components[i] == Type1)
					matches++;
				if (components[i] == Type2)
					matches++;
				if (components[i] == Type3)
					matches++;
				if (components[i] == Type4)
					matches++;
			}

			return matches >= 4;
		}

		public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref TSnapshot snapshot)
		{
			var component1 = chunk.GetNativeArray(component1Type);
			var component2 = chunk.GetNativeArray(component2Type);
			var component3 = chunk.GetNativeArray(component3Type);
			var component4 = chunk.GetNativeArray(component4Type);

			snapshot.Set(component1[ent], component2[ent], component3[ent], component4[ent]);
		}
	}
#endif
}
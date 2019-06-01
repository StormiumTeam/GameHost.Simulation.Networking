using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace DefaultNamespace
{
	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	public abstract class AddSingleComponentSerializer<TComponent, TSnapshot> : ComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent>
	{
		public int SerializerId { get; private set; }

		protected override void OnCreate()
		{
			base.OnCreate();

			World.GetOrCreateSystem<SingleComponentSerializer<TComponent,TSnapshot>.SystemGhostSerializer>();
			World.GetOrCreateSystem<GhostSerializerCollectionSystem>().TryAdd<SingleComponentSerializer<TComponent, TSnapshot>, TSnapshot>(out var serializer);
			SerializerId = serializer.Header.Id;
		}

		protected override void OnUpdate()
		{
		}
	}
	
	public struct SingleComponentSerializer<TComponent, TSnapshot> : IGhostSerializer<TSnapshot>
		where TComponent : struct, IComponentData
		where TSnapshot : unmanaged, ISnapshotFromComponent<TSnapshot, TComponent>
	{
		public GhostSerializerHeader Header { get; }


		public ArchetypeChunkComponentType<TComponent> ghostTestType;

		public void BeginSerialize(ComponentSystemBase system)
		{
			ghostTestType = system.GetArchetypeChunkComponentType<TComponent>();

			Type = typeof(TComponent);
		}

		public void BeginDeserialize(JobComponentSystem system)
		{
			NewGhosts   = system.World.GetExistingSystem<SystemGhostSerializer>().NewGhosts;
			NewGhostIds = system.World.GetExistingSystem<SystemGhostSerializer>().NewGhostIds;
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

		public class SystemGhostSerializer : BaseGhostManageSerializer<TSnapshot, SingleComponentSerializer<TComponent, TSnapshot>>
		{
			public override ComponentTypes GetComponents()
			{
				return new ComponentTypes(typeof(TComponent), typeof(TSnapshot));
			}
		}
	}
}
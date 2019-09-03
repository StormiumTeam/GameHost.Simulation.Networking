using System;
using System.Collections.Generic;
using System.Linq;
using DefaultNamespace;
using K4os.Compression.LZ4;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Revolution.Tests
{
	// There is a world that send snapshot and a world that receive it.
	// The systems are manually created (so if you create serializers to test, you need to also add them here)
	// For further real tests, it would be better to test them in a Client/Server world.

	public class Bootstrap : ICustomBootstrap
	{
		public static World SenderWorld, ReceiverWorld;

		private void AddSerializers(World world)
		{
			var delegateGroup = world.GetOrCreateSystem<SnapshotWithDelegateSystemGroup>();
			delegateGroup.AddSystemToUpdateList(world.GetOrCreateSystem<TestCustomArchetypeSerializer>());
			delegateGroup.AddSystemToUpdateList(world.GetOrCreateSystem<TranslationSnapshotSystem>());
			delegateGroup.AddSystemToUpdateList(world.GetOrCreateSystem<HealthSnapshotSystem>());
		}

		public List<Type> Initialize(List<Type> systems)
		{
			if (SenderWorld != null)
				return systems;

			// Create two different world, a sender and a receiver.
			SenderWorld = new World("SenderWorld");
			SenderWorld.GetOrCreateSystem<SenderSystem>();
			AddSerializers(SenderWorld);

			ReceiverWorld = new World("ReceiverWorld");
			ReceiverWorld.GetOrCreateSystem<ReceiverSystem>();
			AddSerializers(ReceiverWorld);
			ReceiverWorld.GetOrCreateSystem<TranslationUpdateSystem>();

			// --
			// If you need to make sure both versions of the world have the same systems, in the right order
			// use a fixed system collection:
			SenderWorld.GetOrCreateSystem<SnapshotManager>().SetFixedSystems(new SnapshotSystemCollection().Return(SenderWorld));
			ReceiverWorld.GetOrCreateSystem<SnapshotManager>().SetFixedSystems(new SnapshotSystemCollection().Return(ReceiverWorld));

			return systems;
		}
	}

	public class UpdateWorldSystem : ComponentSystem
	{
		protected override void OnUpdate()
		{
			if (Bootstrap.SenderWorld == null)
				return;

			var receiverSystem = Bootstrap.ReceiverWorld.GetExistingSystem<ReceiverSystem>();
			var senderSystem   = Bootstrap.SenderWorld.GetExistingSystem<SenderSystem>();

			senderSystem.ClientRequestData = true;
			senderSystem.Update();

			receiverSystem.Data = senderSystem.OutputData;
			receiverSystem.Update();
		}
	}

	[DisableAutoCreation]
	[AlwaysUpdateSystem]
	public class SenderSystem : ComponentSystem
	{
		public bool                ClientRequestData;
		public DynamicBuffer<byte> OutputData;

		private Dictionary<Entity, SerializeClientData> m_ClientData;
		private CreateSnapshotSystem                    m_CreateSnapshotSystem;

		private EntityQuery m_Query;

		public uint tick;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_ClientData           = new Dictionary<Entity, SerializeClientData>();
			m_CreateSnapshotSystem = World.GetOrCreateSystem<CreateSnapshotSystem>();

			EntityManager.CreateEntity(typeof(Translation), typeof(GhostEntity));
			EntityManager.CreateEntity(typeof(Health), typeof(GhostEntity));
			EntityManager.CreateEntity(typeof(Translation), typeof(Health), typeof(GhostEntity));
			EntityManager.CreateEntity(typeof(CustomArchetypeTag),
				typeof(Translation), typeof(Health),
				typeof(TranslationSnapshotSystem.Exclude), typeof(HealthSnapshotSystem.Exclude),
				typeof(GhostEntity));

			var client = EntityManager.CreateEntity(typeof(ClientSnapshotBuffer));
			m_ClientData[client] = new SerializeClientData(Allocator.Persistent);

			m_Query = GetEntityQuery(typeof(GhostEntity));
		}

		protected override unsafe void OnUpdate()
		{
			if (Input.GetKeyDown(KeyCode.E))
			{
				var random = Random.Range(1, 10);
				for (var i = 0; i != random; i++)
				{
					if (Random.Range(0, 5) > 2)
					{
						EntityManager.CreateEntity(typeof(Health), typeof(GhostEntity));
					}
					else
					{
						EntityManager.CreateEntity(typeof(Translation), typeof(GhostEntity));
					}
				}
			}

			if (Input.GetKeyDown(KeyCode.T))
			{
				EntityManager.CreateEntity(typeof(Health), typeof(GhostEntity));
			}

			if (Input.GetKeyDown(KeyCode.D))
			{
				using (var entities = m_Query.ToEntityArray(Allocator.TempJob))
				{
					var random = Random.Range(0, 5);
					for (var i = 0; i != random; i++)
					{
						var ent = Random.Range(0, entities.Length);
						EntityManager.DestroyEntity(entities[ent]);
					}
				}
			}

			Entities.ForEach((Entity entity, ref Translation translation) =>
			{
				var h = Input.GetAxisRaw("Horizontal");
				var v = Input.GetAxisRaw("Vertical");

				translation.Value.x += h * Time.deltaTime;
				translation.Value.y += v * Time.deltaTime;

				Debug.DrawRay(translation.Value, Vector3.left, Color.blue);
				Debug.DrawRay(translation.Value, Vector3.right, Color.blue);
				Debug.DrawRay(translation.Value, Vector3.up, Color.blue);
				Debug.DrawRay(translation.Value, Vector3.down, Color.blue);
				Debug.DrawRay(translation.Value, new Vector3(h, v), Color.red);

				if (Input.GetKeyDown(KeyCode.U) && EntityManager.HasComponent<Health>(entity))
				{
					Debug.Log("Removed [Health]");
					EntityManager.RemoveComponent<Health>(entity);
				}

				if (Input.GetKeyDown(KeyCode.I) && !EntityManager.HasComponent<Health>(entity))
				{
					Debug.Log("Added [Health]");
					EntityManager.AddComponent<Health>(entity);
				}
			});

			// Create snapshot...
			if (ClientRequestData)
			{
				ClientRequestData = false;
				m_CreateSnapshotSystem.CreateSnapshot(tick, m_ClientData);

				OutputData = EntityManager.GetBuffer<ClientSnapshotBuffer>(m_ClientData.First().Key).Reinterpret<byte>();

				// -- This is just a test to see if it's worth or not to use LZ4 compression.
				// -- ...
				// -- It's worth	(a somewhat idle snapshot with a size of 2000bytes can go to ~100bytes)
				//					(an active snapshot with a size of 2000bytes can go to ~300-500bytes)
				var targetLength = LZ4Codec.MaximumOutputSize(OutputData.Length);
				var target       = UnsafeUtility.Malloc(targetLength, 4, Allocator.Persistent);
				{
					var encodedLength = LZ4Codec.Encode
					(
						(byte*) OutputData.GetUnsafePtr(), OutputData.Length,
						(byte*) target, targetLength
					);

					Debug.Log($"{OutputData.Length} -> {encodedLength}");
				}
				UnsafeUtility.Free(target, Allocator.Persistent);
			}

			tick++;
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			foreach (var data in m_ClientData)
				data.Value.Dispose();
			m_ClientData.Clear();
		}
	}

	[DisableAutoCreation]
	public class ReceiverSystem : ComponentSystem
	{
		public DynamicBuffer<byte> Data;

		private DeserializeClientData m_ReceiverData;
		private ApplySnapshotSystem   m_ApplySnapshotSystem;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_ReceiverData        = new DeserializeClientData(Allocator.Persistent);
			m_ApplySnapshotSystem = World.GetOrCreateSystem<ApplySnapshotSystem>();
		}

		protected override void OnUpdate()
		{
			m_ApplySnapshotSystem.ApplySnapshot(ref m_ReceiverData, Data.AsNativeArray());

			World.GetExistingSystem<SnapshotWithDelegateSystemGroup>().Update();
			World.GetExistingSystem<TranslationUpdateSystem>().Update();
			
			Entities.ForEach((ref Translation translation) =>
			{
				Debug.DrawRay(translation.Value, Vector3.left, Color.green);
				Debug.DrawRay(translation.Value, Vector3.right, Color.green);
				Debug.DrawRay(translation.Value, Vector3.up, Color.green);
				Debug.DrawRay(translation.Value, Vector3.down, Color.green);
			});
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();

			m_ReceiverData.Dispose();
		}
	}
}
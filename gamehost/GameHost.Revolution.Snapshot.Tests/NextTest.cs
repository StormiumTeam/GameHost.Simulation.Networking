using System;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Revolution.NetCode.Next;
using GameHost.Revolution.NetCode.Next.Data;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using NUnit.Framework;

namespace GameHost.Revolution.NetCode.Tests
{
	public class NextTest
	{
		[Test]
		public void TestEntity()
		{
			using var world = new World();
			using var gw    = new GameWorld();

			var entities = new GameEntity[3]
			{
				gw.Safe(gw.CreateEntity()),
				gw.Safe(gw.CreateEntity()),
				gw.Safe(gw.CreateEntity())
			};

			var snapshot = new SnapshotFrame(world);
			var writer   = snapshot.GetWriter();
			writer.AddEntity(entities[0]);
			writer.AddEntity(entities[1]);
			writer.AddEntity(entities[2]);
			writer.BuildInWritingContext(gw);

			Assert.AreEqual(snapshot.Entities[0], entities[0]);
			Assert.AreEqual(snapshot.Entities[1], entities[1]);
			Assert.AreEqual(snapshot.Entities[2], entities[2]);
		}

		[Test]
		public void TestEntityDeserialization()
		{
			using var world = new World();
			using var gw    = new GameWorld();

			var entities = new GameEntity[3]
			{
				gw.Safe(gw.CreateEntity()),
				gw.Safe(gw.CreateEntity()),
				gw.Safe(gw.CreateEntity())
			};

			var snapshot = new SnapshotFrame(world);
			var writer   = snapshot.GetWriter();
			foreach (var ent in entities)
				writer.AddEntity(ent);
			
			writer.BuildInWritingContext(gw);

			Assert.AreEqual(entities, snapshot.Entities.ToArray());

			var bytes       = new PooledList<byte>();
			var snapshotMgr = new SimpleSnapshotManager();
			snapshotMgr.Serialize(snapshot, bytes);
			
			var readSnapshot = new SnapshotFrame(world);
			snapshotMgr.Deserialize(ref readSnapshot, bytes.Span);
			
			Assert.AreEqual(entities, readSnapshot.Entities.ToArray());
		}

		[Test]
		public void TestSystem()
		{
			using var world = new World();

			var snapshot = new SnapshotFrame(world);
			var writer   = snapshot.GetWriter();

			var system = new __TestSystem();
			writer.AddSystem(1, system);

			Assert.IsFalse(system.HasBeenExecuted);
			writer.BuildInWritingContext(null);

			Assert.IsTrue(system.HasBeenExecuted);
		}

		class __TestSystem : ISnapshotSystem
		{
			public bool HasBeenExecuted;
			
			public void PrepareWrite(SnapshotFrameWriter writer, Entity storage)
			{
				HasBeenExecuted = true;
			}

			public void WriteBuffer(BitBuffer buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void PrepareRead(SnapshotFrameWriter  writer, Entity storage)
			{
			}

			public void ReadBuffer(BitBuffer             buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void CompleteRead(SnapshotFrameWriter writer, Entity storage)
			{
			}
		}
		
		[Test]
		public void TestEntityHistory()
		{
			using var world = new World();
			using var gw    = new GameWorld();

			var entities = new[]
			{
				gw.Safe(gw.CreateEntity())
			};

			var previous = new SnapshotFrame(world);
			var next     = new SnapshotFrame(world);

			var componentValues = new int[] {0, 1};

			{
				var writer = previous.GetWriter();
				writer.AddEntity(entities[0]);
				writer.AddSystem(1, new __TestEntityHistorySystem(componentValues));
				
				writer.BuildInWritingContext(gw);
			}

			Assert.AreEqual(previous.SystemStorage[1].Get<Dictionary<GameEntityHandle, int>>()[new (1)], componentValues[1]);
			
			componentValues[1] = 2;

			{
				var writer = next.GetWriter();
				
				writer.AddEntity(entities[0]);
				writer.AddSystem(1, new __TestEntityHistorySystem(componentValues));
				
				writer.BuildInWritingContext(gw);
			}
			
			Assert.AreEqual(previous.SystemStorage[1].Get<Dictionary<GameEntityHandle, int>>()[new (1)], (int) componentValues[1] - 1);
			Assert.AreEqual(next.SystemStorage[1].Get<Dictionary<GameEntityHandle, int>>()[new (1)], (int) componentValues[1]);
		}

		class __TestEntityHistorySystem : ISnapshotSystem
		{
			private readonly int[] componentValues;
			
			public __TestEntityHistorySystem(int[] componentValues)
			{
				this.componentValues = componentValues;
			}
			
			public void PrepareWrite(SnapshotFrameWriter writer, Entity storage)
			{
				if (false == storage.TryGet(out Dictionary<GameEntityHandle, int> map))
					storage.Set(map = new());

				foreach (var entity in writer.Entities)
				{
					map[entity.Handle] = componentValues[entity.Handle.Id];
				}
			}

			public void WriteBuffer(BitBuffer buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void PrepareRead(SnapshotFrameWriter  writer, Entity storage)
			{
			}

			public void ReadBuffer(BitBuffer             buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void CompleteRead(SnapshotFrameWriter writer, Entity storage)
			{
			}
		}

		class __TestComponentSerializationSystem : ISnapshotSystem, ISnapshotSystemSupportArchetype
		{
			public readonly GameWorld GameWorld;
			
			public __TestComponentSerializationSystem(GameWorld gameWorld)
			{
				GameWorld = gameWorld;
			}
			
			public void PrepareWrite(SnapshotFrameWriter writer, Entity storage)
			{
			}

			public void WriteBuffer(BitBuffer            buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void PrepareRead(SnapshotFrameWriter  writer, Entity storage)
			{
			}

			public void ReadBuffer(BitBuffer             buffer, Entity storage, Entity? baselineStorage)
			{
			}

			public void CompleteRead(SnapshotFrameWriter writer, Entity storage)
			{
			}

			public ISerializerArchetype GetSerializerArchetype()
			{
				return new SimpleSerializerArchetype()
			}
		}
	}
}
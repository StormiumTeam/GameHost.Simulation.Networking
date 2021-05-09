using DefaultEcs;
using GameHost.Revolution.NetCode.Next;
using GameHost.Revolution.NetCode.Next.Implementations;
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

			var snapshot = new SnapshotFrame(world);
			using (var builder = new SnapshotBuilder())
			{
				builder.SetOutput(snapshot);
				builder.AddEntity(new(1, 16));
				builder.AddEntity(new(2, 17));
				builder.AddEntity(new(3, 18));

				builder.Complete();
			}

			Assert.AreEqual(snapshot.Entities[0], new GameEntity(1, 16));
			Assert.AreEqual(snapshot.Entities[2], new GameEntity(2, 17));
			Assert.AreEqual(snapshot.Entities[3], new GameEntity(3, 18));
		}
	}
}
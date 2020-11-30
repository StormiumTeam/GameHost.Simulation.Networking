using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Core.Threading;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.Networking.Tests;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.TabEcs.Interfaces;
using NUnit.Framework;

namespace GameHost.Revolution.Snapshot.Tests
{
	public class Tests
	{
		[SetUp]
		public void Setup()
		{
		}

		[Test]
		public void Test1()
		{
			var buffer = new BitBuffer();
			buffer.AddBool(true);
			buffer.AddBool(false);
			buffer.AddBool(false);
			buffer.AddBool(true);
			Console.WriteLine(buffer.Length);
		}

		[Test]
		public void AlignTest()
		{
			var buffer = new BitBuffer();
			buffer.AddBool(true);
			buffer.AddAlign();
			Assert.AreEqual(sizeof(uint) * 8, buffer.nextPosition);
			
			buffer.Add(32, 0);
			buffer.AddAlign();
			Assert.AreEqual(sizeof(uint) * 8 * 2, buffer.nextPosition);

			buffer.ReadBool();
			buffer.ReadAlign();
			Assert.AreEqual(sizeof(uint) * 8, buffer.readPosition);
			
			buffer.Read(32);
			buffer.ReadAlign();
			Assert.AreEqual(sizeof(uint) * 8 * 2, buffer.readPosition);
		}

		[Test]
		public void SpanTest()
		{
			var source = new byte[] {42, 43, 44, 8};

			var buffer = new BitBuffer(16);
			buffer.AddBool(true);
			buffer.AddSpan(source);
			buffer.AddInt(16);
			buffer.AddSpan(source);
			buffer.AddSpan(source);
			Console.WriteLine(buffer);

			var receive = new byte[source.Length];
			buffer.ReadBool();
			buffer.ReadSpan(receive, receive.Length);
			Assert.AreEqual(16, buffer.ReadInt());
			Assert.AreEqual(source, receive);
			buffer.ReadSpan(receive, receive.Length);
			Assert.AreEqual(source, receive);
			buffer.ReadSpan(receive, receive.Length);
			Assert.AreEqual(source, receive);
		}

		[Test]
		public void TestDelta()
		{
			var buffer = new BitBuffer();

			var array = new uint[] {0, 8, 12, 5, 4, 8};
			var prev  = 0u;
			foreach (var t in array)
			{
				buffer.AddUIntD4Delta(t, prev);
				prev = t;
			}

			prev = default;
			foreach (var t in array)
			{
				prev = buffer.ReadUIntD4Delta(prev);
				Assert.AreEqual(prev, t);
			}
		}

		[Test]
		public void TestAdd()
		{
			var buffer = new BitBuffer(1);
			var copy   = new BitBuffer(1);

			buffer.Add(8, 42);
			copy.AddBitBuffer(buffer);

			buffer.Clear();
			copy.ReadToExistingBuffer(buffer);
			Assert.AreEqual(42, buffer.Read(8));
		}

		[Test]
		[Timeout(8000)]
		public void TestSerialization()
		{
			var worldCollection = new WorldCollection(null, new World());
			var gameWorld       = new GameWorld();

			worldCollection.Ctx.BindExisting<IScheduler>(new Scheduler());
			worldCollection.Ctx.BindExisting(gameWorld);

			var serializer = new BroadcastInstigator(null, 0, worldCollection.Ctx);
			Assert.IsTrue(serializer.Storage.IsAlive);

			serializer.AddSerializer(new TestSerializer(serializer, serializer.Context));

			while (serializer.DependencyResolver.Dependencies.Any())
				worldCollection.Ctx.Container
				               .GetOrDefault<IScheduler>()
				               .Run();

			var globalEntity = gameWorld.CreateEntity();
			gameWorld.AddComponent(globalEntity, new TestComponent {Position = {X = 48652, Y = 1512, Z = 0}});

			var client = serializer.AddClient(1);

			foreach (var ent in gameWorld.Boards.Entity.Alive) Console.WriteLine(gameWorld.Safe(ent));

			void serialize()
			{
				serializer.Serialize(0);
			}

			void deserialize()
			{
				var clientData = client.GetClientData();
				var array = new byte[clientData.Length];
				clientData.ToSpan(array);
				client.Deserialize(array);
			}

			var queued = serializer.QueuedEntities;
			queued[gameWorld.Safe(globalEntity)] = EntitySnapshotPriority.NoPriority;

			serialize();
			deserialize();

			Assert.IsFalse(client.State.GetAllEntities().IsEmpty);
			foreach (var entity in client.State.GetAllEntities())
			{
				var handle = new GameEntityHandle(entity);

				Assert.IsTrue(gameWorld.HasComponent<SnapshotEntity>(handle));
				Assert.IsTrue(gameWorld.HasComponent<TestSnapshot>(handle));

				Assert.AreEqual(1, gameWorld.GetBuffer<TestSnapshot>(handle).Count);

				var snapshotData = gameWorld.GetBuffer<TestSnapshot>(handle)[0];

				TestComponent component = default;
				snapshotData.ToComponent(ref component);

				Assert.AreEqual(gameWorld.GetComponentData<TestComponent>(globalEntity).Position, component.Position);
			}
			
			Span<GameEntityHandle> entities = new GameEntityHandle[1000];
			gameWorld.CreateEntityBulk(entities);
			foreach (var ent in entities)
			{
				gameWorld.AddComponent(ent, new TestComponent {Position                       = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<byte>.TestComponent {Position       = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<short>.TestComponent {Position      = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<int>.TestComponent {Position        = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<long>.TestComponent {Position       = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<float>.TestComponent {Position      = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<double>.TestComponent {Position     = {X = ent.Id}});
				gameWorld.AddComponent(ent, new Component<ValueTuple>.TestComponent {Position = {X = ent.Id}});
			}

			serializer.AddSerializer(new Component<byte>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<short>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<int>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<long>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<float>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<double>.TestSerializer(serializer, serializer.Context));
			serializer.AddSerializer(new Component<ValueTuple>.TestSerializer(serializer, serializer.Context));

			worldCollection.Ctx.Container
			               .GetOrDefault<IScheduler>()
			               .Run();


			var sw             = new Stopwatch();
			var minSerialize   = TimeSpan.MaxValue;
			var minDeserialize = TimeSpan.MaxValue;
			for (var i = 0; i < 1000; i++)
			{
				sw.Restart();
				benchmarkSerialize(gameWorld, entities, (uint) i, serializer);
				sw.Stop();
				minSerialize = new TimeSpan(Math.Min(sw.Elapsed.Ticks, minSerialize.Ticks));
				sw.Restart();
				benchmarkDeserialize(client);
				sw.Stop();
				minDeserialize = new TimeSpan(Math.Min(sw.Elapsed.Ticks, minDeserialize.Ticks));

				var positionAccessor = new ComponentDataAccessor<TestComponent>(gameWorld);
				foreach (var ent in entities)
					positionAccessor[ent].Position.X += 0.1f;
			}

			Console.WriteLine($"Serialize={minSerialize.TotalMilliseconds}ms, Deserialize={minDeserialize.TotalMilliseconds}ms");
		}

		private void benchmarkSerialize(GameWorld gameWorld, Span<GameEntityHandle> entities, uint i, BroadcastInstigator instigator)
		{
			var queued = instigator.QueuedEntities;
			foreach (var ent in entities)
				queued[gameWorld.Safe(ent)] = EntitySnapshotPriority.SendAtAllCost;
			instigator.Serialize(i);
		}

		private void benchmarkDeserialize(ClientSnapshotInstigator instigator)
		{
			var clientData = instigator.GetClientData();

			Span<byte> span = new byte[clientData.Length];
			clientData.ReadSpan(span, span.Length);
			instigator.Deserialize(span);
		}

		public struct TestComponent : IComponentData
		{
			public Vector3 Position;
		}

		public struct TestSnapshot : IReadWriteSnapshotData<TestSnapshot>, ISnapshotSyncWithComponent<TestComponent>
		{
			public const int   Quantization   = 100;
			public const float Dequantization = 0.01f;

			public uint Tick { get; set; }

			public int X, Y, Z;

			public void Serialize(in BitBuffer buffer, in TestSnapshot baseline)
			{
				buffer.AddIntDelta(X, baseline.X)
				      .AddIntDelta(Y, baseline.Y)
				      .AddIntDelta(Z, baseline.Z);
			}

			public void Deserialize(in BitBuffer buffer, in TestSnapshot baseline)
			{
				X = buffer.ReadIntDelta(baseline.X);
				Y = buffer.ReadIntDelta(baseline.Y);
				Z = buffer.ReadIntDelta(baseline.Z);
			}

			public void FromComponent(in TestComponent component)
			{
				X = (int) (component.Position.X * Quantization);
				Y = (int) (component.Position.Y * Quantization);
				Z = (int) (component.Position.Z * Quantization);
			}

			public void ToComponent(ref TestComponent component)
			{
				component.Position.X = X * Dequantization;
				component.Position.Y = Y * Dequantization;
				component.Position.Z = Z * Dequantization;
			}
		}

		public class TestSerializer : DeltaComponentSerializerBase<TestSnapshot, TestComponent>
		{
			public TestSerializer(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
			{
			}
		}
	}
}
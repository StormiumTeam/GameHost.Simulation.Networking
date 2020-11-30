using System.Numerics;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Simulation.Networking.Tests
{
	public static class Component<T>
	{
		public struct TestComponent : IComponentData
		{
			public T       val;
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
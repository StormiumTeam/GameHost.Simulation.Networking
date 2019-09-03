using System;
using System.Runtime.InteropServices;
using Revolution;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace DefaultNamespace
{
	// -- 
	// This is a very basic example to show how to synchronize a very basic component, like health.
	
	public struct Health : IComponentData
	{
		public struct Snapshot : IReadWriteSnapshot<Snapshot>, ISynchronizeImpl<Health>
		{
			public uint Tick { get; set; }
			public int  Value, Max;

			public void WriteTo(DataStreamWriter writer, ref Snapshot baseline, NetworkCompressionModel compressionModel)
			{
				writer.WritePackedIntDelta(Value, baseline.Value, compressionModel);
				writer.WritePackedIntDelta(Max, baseline.Max, compressionModel);
			}

			public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref Snapshot baseline, NetworkCompressionModel compressionModel)
			{
				Value = reader.ReadPackedIntDelta(ref ctx, baseline.Value, compressionModel);
				Max   = reader.ReadPackedIntDelta(ref ctx, baseline.Max, compressionModel);
			}

			public void SynchronizeFrom(in Health component)
			{
				Value = component.Value;
				Max   = component.Value;
			}

			public void SynchronizeTo(ref Health component)
			{
				component.Value = Value;
				component.Max   = Max;
			}
		}

		public int Value, Max;
	}

	public class HealthSnapshotSystem : ComponentSnapshotSystem_Basic<Health, Health.Snapshot>
	{
		public struct Exclude : IComponentData
		{
		}

		public override ComponentType ExcludeComponent => typeof(Exclude);
	}
}
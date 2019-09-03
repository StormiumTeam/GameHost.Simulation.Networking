using Revolution;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEngine;

namespace DefaultNamespace
{
	// --
	// This is an example to show how to synchronize the translation in a snapshot
	// It also implement ISnapshotDelta to demonstrate how to use delta change (TranslationSnapshot.DidChange(...))
	
	public struct TranslationSnapshot : IReadWriteSnapshot<TranslationSnapshot>, ISynchronizeImpl<Translation>, ISnapshotDelta<TranslationSnapshot>
	{
		public uint Tick { get; set; }

		public int3 Vector;

		public void WriteTo(DataStreamWriter writer, ref TranslationSnapshot baseline, NetworkCompressionModel compressionModel)
		{
			for (var i = 0; i != 3; i++)
				writer.WritePackedIntDelta(Vector[i], baseline.Vector[i], compressionModel);
		}

		public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref TranslationSnapshot baseline, NetworkCompressionModel compressionModel)
		{
			for (var i = 0; i != 3; i++)
				Vector[i] = reader.ReadPackedIntDelta(ref ctx, baseline.Vector[i], compressionModel);
		}

		public void SynchronizeFrom(in Translation component)
		{
			Vector = (int3) (component.Value * 1000);
		}

		public void SynchronizeTo(ref Translation component)
		{
			component.Value = (float3) Vector * 0.001f;
		}

		public bool DidChange(TranslationSnapshot baseline)
		{
			return math.distance(Vector, baseline.Vector) > 10;
		}
	}

	public class TranslationSnapshotSystem : ComponentSnapshotSystem_Delta<Translation, TranslationSnapshot>
	{
		public struct Exclude : IComponentData
		{
		}

		public override ComponentType ExcludeComponent => typeof(Exclude);
	}

	public class TranslationUpdateSystem : ComponentUpdateSystem<Translation, TranslationSnapshot>
	{
	}
}
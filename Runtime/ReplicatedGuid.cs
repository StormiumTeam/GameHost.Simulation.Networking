using System;
using Unity.Entities;
using Unity.Networking.Transport;

namespace DefaultNamespace
{
	[Serializable]
	public struct AssetGuid : IComponentData
	{
		public ReplicatedGuid value;
	}

	[Serializable]
	public struct ReplicatedGuid : IEquatable<ReplicatedGuid>
	{
		public uint b0, b1, b2, b3;

		public void Write(DataStreamWriter writer)
		{
			writer.Write(b0);
			writer.Write(b1);
			writer.Write(b2);
			writer.Write(b3);
		}

		public void WritePacked(DataStreamWriter writer, NetworkCompressionModel compressionModel)
		{
			writer.WritePackedUInt(b0, compressionModel);
			writer.WritePackedUInt(b1, compressionModel);
			writer.WritePackedUInt(b2, compressionModel);
			writer.WritePackedUInt(b3, compressionModel);
		}

		public void WritePackedDelta(DataStreamWriter writer, ReplicatedGuid baseline, NetworkCompressionModel compressionModel)
		{
			writer.WritePackedUIntDelta(b0, baseline.b0, compressionModel);
			writer.WritePackedUIntDelta(b1, baseline.b1, compressionModel);
			writer.WritePackedUIntDelta(b2, baseline.b2, compressionModel);
			writer.WritePackedUIntDelta(b3, baseline.b3, compressionModel);
		}

		public void Read(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			b0 = reader.ReadUInt(ref ctx);
			b1 = reader.ReadUInt(ref ctx);
			b2 = reader.ReadUInt(ref ctx);
			b3 = reader.ReadUInt(ref ctx);
		}

		public void ReadPacked(DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			b0 = reader.ReadPackedUInt(ref ctx, compressionModel);
			b1 = reader.ReadPackedUInt(ref ctx, compressionModel);
			b2 = reader.ReadPackedUInt(ref ctx, compressionModel);
			b3 = reader.ReadPackedUInt(ref ctx, compressionModel);
		}

		public void ReadPackedDelta(DataStreamReader reader, ref DataStreamReader.Context ctx, ReplicatedGuid baseline, NetworkCompressionModel compressionModel)
		{
			b0 = reader.ReadPackedUIntDelta(ref ctx, baseline.b0, compressionModel);
			b1 = reader.ReadPackedUIntDelta(ref ctx, baseline.b1, compressionModel);
			b2 = reader.ReadPackedUIntDelta(ref ctx, baseline.b2, compressionModel);
			b3 = reader.ReadPackedUIntDelta(ref ctx, baseline.b3, compressionModel);
		}

		public bool Equals(ReplicatedGuid other)
		{
			return b0 == other.b0
			       && b1 == other.b1
			       && b2 == other.b2
			       && b3 == other.b3;
		}
	}
}
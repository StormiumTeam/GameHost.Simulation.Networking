using System;
using Unity.Entities;
using Unity.Networking.Transport;

namespace DefaultNamespace
{
	public struct ReplSceneId : IComponentData
	{
		public uint B0, B1, B2, B3;

		public void Write(DataStreamWriter writer)
		{
			writer.Write(B0);
			writer.Write(B1);
			writer.Write(B2);
			writer.Write(B3);
		}
		
		public void WritePacked(DataStreamWriter writer, NetworkCompressionModel compressionModel)
		{
			writer.WritePackedUInt(B0, compressionModel);
			writer.WritePackedUInt(B1, compressionModel);
			writer.WritePackedUInt(B2, compressionModel);
			writer.WritePackedUInt(B3, compressionModel);
		}

		public void WritePackedDelta(DataStreamWriter writer, ReplSceneId baseline, NetworkCompressionModel compressionModel)
		{
			writer.WritePackedUIntDelta(B0, baseline.B0, compressionModel);
			writer.WritePackedUIntDelta(B1, baseline.B1, compressionModel);
			writer.WritePackedUIntDelta(B2, baseline.B2, compressionModel);
			writer.WritePackedUIntDelta(B3, baseline.B3, compressionModel);
		}

		public void Read(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			B0 = reader.ReadUInt(ref ctx);
			B1 = reader.ReadUInt(ref ctx);
			B2 = reader.ReadUInt(ref ctx);
			B3 = reader.ReadUInt(ref ctx);
		}
		
		public void ReadPacked(DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			B0 = reader.ReadPackedUInt(ref ctx, compressionModel);
			B1 = reader.ReadPackedUInt(ref ctx, compressionModel);
			B2 = reader.ReadPackedUInt(ref ctx, compressionModel);
			B3 = reader.ReadPackedUInt(ref ctx, compressionModel);
		}
		
		public void ReadPackedDelta(DataStreamReader reader, ref DataStreamReader.Context ctx, ReplSceneId baseline, NetworkCompressionModel compressionModel)
		{
			B0 = reader.ReadPackedUIntDelta(ref ctx, baseline.B0, compressionModel);
			B1 = reader.ReadPackedUIntDelta(ref ctx, baseline.B1, compressionModel);
			B2 = reader.ReadPackedUIntDelta(ref ctx, baseline.B2, compressionModel);
			B3 = reader.ReadPackedUIntDelta(ref ctx, baseline.B3, compressionModel);
		}
	}
}
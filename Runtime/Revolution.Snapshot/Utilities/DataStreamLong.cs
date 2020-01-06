using package.stormiumteam.shared;
using Unity.Networking.Transport;

namespace Utilities
{
	public static class DataStreamLong
	{
		public static void WritePackedLong(this DataStreamWriter writer, long value, NetworkCompressionModel compression)
		{
			var union = new LongIntUnion {LongValue = value};
			writer.WritePackedInt(union.Int0Value, compression);
			writer.WritePackedInt(union.Int1Value, compression);
		}

		public static long ReadPackedLong(this DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compression)
		{
			var i1 = reader.ReadPackedInt(ref ctx, compression);
			var i2 = reader.ReadPackedInt(ref ctx, compression);
			return new LongIntUnion {Int0Value = i1, Int1Value = i2}.LongValue;
		}
	}
}
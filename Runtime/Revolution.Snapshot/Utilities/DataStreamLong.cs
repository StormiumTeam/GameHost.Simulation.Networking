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

		public static void WritePackedULong(this DataStreamWriter writer, ulong value, NetworkCompressionModel compression)
		{
			var union = new ULongUIntUnion {LongValue = value};
			writer.WritePackedUInt(union.Int0Value, compression);
			writer.WritePackedUInt(union.Int1Value, compression);
		}

		public static ulong ReadPackedULong(this DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compression)
		{
			var i1 = reader.ReadPackedUInt(ref ctx, compression);
			var i2 = reader.ReadPackedUInt(ref ctx, compression);
			return new ULongUIntUnion {Int0Value = i1, Int1Value = i2}.LongValue;
		}

		public static void WritePackedULongDelta(this DataStreamWriter writer, ulong value, ulong baseline, NetworkCompressionModel compression)
		{
			var baselineUnion = new ULongUIntUnion {LongValue = baseline};
			var union         = new ULongUIntUnion {LongValue = value};
			writer.WritePackedUIntDelta(union.Int0Value, baselineUnion.Int0Value, compression);
			writer.WritePackedUIntDelta(union.Int1Value, baselineUnion.Int1Value, compression);
		}

		public static ulong ReadPackedULongDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, ulong baseline, NetworkCompressionModel compression)
		{
			var baselineUnion = new ULongUIntUnion {LongValue = baseline};

			var i1 = reader.ReadPackedUIntDelta(ref ctx, baselineUnion.Int0Value, compression);
			var i2 = reader.ReadPackedUIntDelta(ref ctx, baselineUnion.Int1Value, compression);
			return new ULongUIntUnion {Int0Value = i1, Int1Value = i2}.LongValue;
		}
	}
}
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.Networking.Transport;
using UnityEngine;

namespace Utilities
{
	[StructLayout(LayoutKind.Explicit)]
	struct ShortToUIntUnion
	{
		[FieldOffset(0)]
		public uint UIntValue;

		[FieldOffset(0)]
		public short Short0Value;

		[FieldOffset(sizeof(short))]
		public short Short1Value;
	}

	[StructLayout(LayoutKind.Explicit)]
	struct ShortByteToUIntUnion
	{
		[FieldOffset(0)]
		public uint UIntValue;

		[FieldOffset(0)]
		public byte ByteValue;

		[FieldOffset(sizeof(byte))]
		public short ShortValue;
	}

	public struct CompressedQuaternion
	{
		public byte  MaxIndex;
		public short A;
		public short B;
		public short C;

		private const float FloatPrecision = 10000f;

		public CompressedQuaternion(quaternion quaternion)
		{
			MaxIndex = 0;
			A        = 0;
			B        = 0;
			C        = 0;
			Quaternion    = quaternion;
		}

		public quaternion Quaternion
		{
			get
			{
				if (MaxIndex >= 4 && MaxIndex <= 7)
				{
					var x = MaxIndex == 4 ? 1f : 0f;
					var y = MaxIndex == 5 ? 1f : 0f;
					var z = MaxIndex == 6 ? 1f : 0f;
					var w = MaxIndex == 7 ? 1f : 0f;

					return new Quaternion(x, y, z, w);
				}

				var d = Mathf.Sqrt(1f - (A * A + B * B + C * C));

				if (MaxIndex == 0)
					return new Quaternion(d, A, B, C);
				if (MaxIndex == 1)
					return new Quaternion(A, d, B, C);
				if (MaxIndex == 2)
					return new Quaternion(A, B, d, C);

				return new Quaternion(A, B, C, d);
			}
			set
			{
				MaxIndex = 0;
				A        = 0;
				B        = 0;
				C        = 0;

				var maxValue = float.MinValue;
				var sign     = 1f;

				for (byte i = 0; i < 4; i++)
				{
					var element = value.value[i];
					var abs     = Mathf.Abs(value.value[i]);
					if (abs > maxValue)
					{
						sign = element < 0 ? -1 : 1;

						MaxIndex = i;
						maxValue = abs;
					}
				}

				if (Mathf.Approximately(maxValue, 1f))
				{
					MaxIndex += 4;
					A        =  0;
					B        =  0;
					C        =  0;
					return;
				}

				if (MaxIndex == 0)
				{
					A = (short) (value.value.y * sign * FloatPrecision);
					B = (short) (value.value.z * sign * FloatPrecision);
					C = (short) (value.value.w * sign * FloatPrecision);
				}
				else if (MaxIndex == 1)
				{
					A = (short) (value.value.x * sign * FloatPrecision);
					B = (short) (value.value.z * sign * FloatPrecision);
					C = (short) (value.value.w * sign * FloatPrecision);
				}
				else if (MaxIndex == 2)
				{
					A = (short) (value.value.x * sign * FloatPrecision);
					B = (short) (value.value.y * sign * FloatPrecision);
					C = (short) (value.value.w * sign * FloatPrecision);
				}
				else
				{
					A = (short) (value.value.x * sign * FloatPrecision);
					B = (short) (value.value.y * sign * FloatPrecision);
					C = (short) (value.value.z * sign * FloatPrecision);
				}
			}
		}
	}

	public static class CompressedQuaternionDataStreamExtension
	{
		public static void WritePackedQuaternion(this DataStreamWriter writer, in CompressedQuaternion quaternion, NetworkCompressionModel compressionModel)
		{
			var union0 = new ShortByteToUIntUnion {ShortValue = quaternion.A, ByteValue   = quaternion.MaxIndex};
			var union1 = new ShortToUIntUnion {Short0Value    = quaternion.B, Short1Value = quaternion.C};

			writer.WritePackedUInt(union0.UIntValue, compressionModel);
			writer.WritePackedUInt(union1.UIntValue, compressionModel);
		}

		public static CompressedQuaternion ReadPackedQuaternion(this DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			var union0 = new ShortByteToUIntUnion {UIntValue = reader.ReadPackedUInt(ref ctx, compressionModel)};
			var union1 = new ShortToUIntUnion {UIntValue     = reader.ReadPackedUInt(ref ctx, compressionModel)};

			CompressedQuaternion quaternion = default;
			quaternion.MaxIndex = union0.ByteValue;
			quaternion.A        = union0.ShortValue;
			quaternion.B        = union1.Short0Value;
			quaternion.C        = union1.Short1Value;
			return quaternion;
		}

		// DELTA

		public static void WritePackedQuaternionDelta(this DataStreamWriter writer, in CompressedQuaternion quaternion, in CompressedQuaternion baseline, NetworkCompressionModel compressionModel)
		{
			var baselineUnion0 = new ShortByteToUIntUnion {ShortValue = baseline.A, ByteValue     = baseline.MaxIndex};
			var baselineUnion1 = new ShortToUIntUnion {Short0Value    = baseline.B, Short1Value   = baseline.C};
			var union0         = new ShortByteToUIntUnion {ShortValue = quaternion.A, ByteValue   = quaternion.MaxIndex};
			var union1         = new ShortToUIntUnion {Short0Value    = quaternion.B, Short1Value = quaternion.C};

			writer.WritePackedUIntDelta(union0.UIntValue, baselineUnion0.UIntValue, compressionModel);
			writer.WritePackedUIntDelta(union1.UIntValue, baselineUnion1.UIntValue, compressionModel);
		}

		public static CompressedQuaternion ReadPackedQuaternionDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, in CompressedQuaternion baseline, NetworkCompressionModel compressionModel)
		{
			var baselineUnion0 = new ShortByteToUIntUnion {ShortValue = baseline.A, ByteValue   = baseline.MaxIndex};
			var baselineUnion1 = new ShortToUIntUnion {Short0Value    = baseline.B, Short1Value = baseline.C};
			var union0         = new ShortByteToUIntUnion {UIntValue  = reader.ReadPackedUIntDelta(ref ctx, baselineUnion0.UIntValue, compressionModel)};
			var union1         = new ShortToUIntUnion {UIntValue      = reader.ReadPackedUIntDelta(ref ctx, baselineUnion1.UIntValue, compressionModel)};

			CompressedQuaternion quaternion = default;
			quaternion.MaxIndex = union0.ByteValue;
			quaternion.A        = union0.ShortValue;
			quaternion.B        = union1.Short0Value;
			quaternion.C        = union1.Short1Value;
			return quaternion;
		}
	}
}
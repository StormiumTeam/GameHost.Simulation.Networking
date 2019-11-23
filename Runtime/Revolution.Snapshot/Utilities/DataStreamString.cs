using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Utilities
{
	public static unsafe class DataStreamString
	{
		public static void WritePackedStringDelta(this DataStreamWriter writer, NativeString64 str, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			var nameAddr = (uint*) UnsafeUtility.AddressOf(ref str) + sizeof(uint);
			var baseAddr = (uint*) UnsafeUtility.AddressOf(ref baseline) + sizeof(uint);

			var charCount = str.LengthInBytes;
			var same      = charCount == baseline.LengthInBytes && UnsafeUtility.MemCmp(nameAddr, baseAddr, sizeof(uint) * NativeString64.MaxLength) == 0;
			if (!same)
			{
				writer.WritePackedUInt(1, compressionModel);
				writer.WritePackedUIntDelta((uint) charCount, (uint) baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
				{
					for (var i = 0; i != charCount; i++)
					{
						writer.WritePackedUIntDelta(nameAddr[i], baseAddr[i], compressionModel);
					}
				}
				else
				{
					for (var i = 0; i != charCount; i++)
					{
						writer.WritePackedUInt(nameAddr[i], compressionModel);
					}
				}
			}
			else
			{
				writer.WritePackedUInt(0, compressionModel);
			}
		}

		public static NativeString64 ReadPackedStringDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			var str      = default(NativeString64);
			var nameAddr = (uint*) UnsafeUtility.AddressOf(ref str) + sizeof(uint);
			var baseAddr = (uint*) UnsafeUtility.AddressOf(ref baseline) + sizeof(uint);

			var same = reader.ReadPackedUInt(ref ctx, compressionModel) == 0;
			if (!same)
			{
				var charCount = reader.ReadPackedUIntDelta(ref ctx, (uint) baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
				{
					for (var i = 0; i != charCount; i++)
					{
						nameAddr[i] = (char) reader.ReadPackedUIntDelta(ref ctx, baseAddr[i], compressionModel);
					}
				}
				else
				{

					str.LengthInBytes = (ushort) charCount;
					for (var i = 0; i != charCount; i++)
					{
						nameAddr[i] = reader.ReadPackedUInt(ref ctx, compressionModel);
					}
				}
			}

			return str;
		}
	}
}
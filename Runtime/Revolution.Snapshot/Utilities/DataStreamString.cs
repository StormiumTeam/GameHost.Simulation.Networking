using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Networking.Transport;

namespace Utilities
{
	public static unsafe class DataStreamString
	{
		//
		// 64
		//
		/*public static void WritePackedStringDelta(this DataStreamWriter writer, NativeString64 str, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			baseline = default;
			
			var nameAddr = (uint*) &str.buffer.byte0000;
			var baseAddr = (uint*) &baseline.buffer.byte0000;

			var charCount = str.LengthInBytes;
			var same      = charCount == baseline.LengthInBytes && UnsafeUtility.MemCmp(nameAddr, baseAddr, sizeof(uint) * NativeString64.MaxLength) == 0;
			if (!same)
			{
				uint prev = 0;
				
				writer.WritePackedUInt(1, compressionModel);
				writer.WritePackedUIntDelta(charCount, baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
					for (var i = 0; i != charCount; i++)
						writer.WritePackedUIntDelta(nameAddr[i], baseAddr[i], compressionModel);
				else
					for (var i = 0; i != charCount; i++)
					{
						writer.WritePackedUIntDelta(nameAddr[i], prev, compressionModel);
						prev = nameAddr[i];
					}
			}
			else
			{
				writer.WritePackedUInt(0, compressionModel);
			}
		}*/

		/*public static NativeString64 ReadPackedStringDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			baseline = default;
			
			var str      = default(NativeString64);
			var nameAddr = (uint*) &str.buffer.byte0000;
			var baseAddr = (uint*) &baseline.buffer.byte0000;

			var same = reader.ReadPackedUInt(ref ctx, compressionModel) == 0;
			if (!same)
			{
				uint prev = 0;
				
				var charCount = reader.ReadPackedUIntDelta(ref ctx, baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
				{
					for (var i = 0; i != charCount; i++) nameAddr[i] = (char) reader.ReadPackedUIntDelta(ref ctx, baseAddr[i], compressionModel);
				}
				else
				{
					str.LengthInBytes = (ushort) charCount;
					for (var i = 0; i != charCount; i++)
					{
						nameAddr[i] = reader.ReadPackedUIntDelta(ref ctx, prev, compressionModel);
						prev = nameAddr[i];
					}
				}
			}
			else
			{
				// if nothing changed, use the baseline
				str = baseline;
			}

			return str;
		}*/
		
		//
		// 512
		//
		/*public static void WritePackedStringDelta(this DataStreamWriter writer, NativeString512 str, NativeString512 baseline, NetworkCompressionModel compressionModel)
		{
			baseline = default;
			
			var nameAddr = (uint*) &str.buffer.byte0000;
			var baseAddr = (uint*) &baseline.buffer.byte0000;

			var charCount = str.LengthInBytes;
			var same      = charCount == baseline.LengthInBytes && UnsafeUtility.MemCmp(nameAddr, baseAddr, sizeof(uint) * NativeString64.MaxLength) == 0;
			if (!same)
			{
				writer.WritePackedUInt(1, compressionModel);
				writer.WritePackedUIntDelta(charCount, baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
					for (var i = 0; i != charCount; i++)
						writer.WritePackedUIntDelta(nameAddr[i], baseAddr[i], compressionModel);
				else
					for (var i = 0; i != charCount; i++)
						writer.WritePackedUInt(nameAddr[i], compressionModel);
			}
			else
			{
				writer.WritePackedUInt(0, compressionModel);
			}
		}

		public static NativeString512 ReadPackedStringDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, NativeString512 baseline, NetworkCompressionModel compressionModel)
		{
			baseline = default;
			
			var str      = default(NativeString512);
			var nameAddr = (uint*) &str.buffer.byte0000;
			var baseAddr = (uint*) &baseline.buffer.byte0000;

			var same = reader.ReadPackedUInt(ref ctx, compressionModel) == 0;
			if (!same)
			{
				var charCount = reader.ReadPackedUIntDelta(ref ctx, baseline.LengthInBytes, compressionModel);
				if (baseline.LengthInBytes == charCount)
				{
					for (var i = 0; i != charCount; i++) nameAddr[i] = (char) reader.ReadPackedUIntDelta(ref ctx, baseAddr[i], compressionModel);
				}
				else
				{
					str.LengthInBytes = (ushort) charCount;
					for (var i = 0; i != charCount; i++) nameAddr[i] = reader.ReadPackedUInt(ref ctx, compressionModel);
				}
			}
			else
			{
				// if nothing changed, use the baseline
				str = baseline;
			}

			return str;
		}*/
	}
}
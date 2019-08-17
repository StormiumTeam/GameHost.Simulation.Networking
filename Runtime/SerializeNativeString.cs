using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;

namespace DefaultNamespace
{
	public static unsafe class SerializeNativeString
	{
		public static void WriteDelta(this DataStreamWriter writer, NativeString64 str, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			var nameAddr = (uint*) UnsafeUtility.AddressOf(ref str) + sizeof(uint);
			var baseAddr = (uint*) UnsafeUtility.AddressOf(ref baseline) + sizeof(uint);

			var charCount = str.Length;
			var same      = str.Length == baseline.Length && UnsafeUtility.MemCmp(nameAddr, baseAddr, sizeof(uint) * NativeString64.MaxLength) == 0;
			if (!same)
			{
				writer.WritePackedUInt(1, compressionModel);
				writer.WritePackedUIntDelta((uint) charCount, (uint) baseline.Length, compressionModel);
				if (baseline.Length == charCount)
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

		public static NativeString64 ReadDelta(this DataStreamReader reader, ref DataStreamReader.Context ctx, NativeString64 baseline, NetworkCompressionModel compressionModel)
		{
			var str = default(NativeString64);
			var nameAddr = (uint*) UnsafeUtility.AddressOf(ref str) + sizeof(uint);
			var baseAddr = (uint*) UnsafeUtility.AddressOf(ref baseline) + sizeof(uint);
			
			var same = reader.ReadPackedUInt(ref ctx, compressionModel) == 0;
			if (!same)
			{
				var charCount = reader.ReadPackedUIntDelta(ref ctx, (uint) baseline.Length, compressionModel);
				if (baseline.Length == charCount)
				{
					for (var i = 0; i != charCount; i++)
					{
						nameAddr[i] = (char) reader.ReadPackedUIntDelta(ref ctx, baseAddr[i], compressionModel);
					}
				}
				else
				{

					str.Length = (int) charCount;
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
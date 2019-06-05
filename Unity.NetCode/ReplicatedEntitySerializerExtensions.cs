using Unity.Entities;
using UnityEngine;

namespace Unity.NetCode
{
	public static class ReplicatedEntitySerializerExtensions
	{
		public static bool HasSerializer(this DynamicBuffer<ReplicatedEntitySerializer> buffer, int target)
		{
			var length = buffer.Length;
			var intArray = buffer.Reinterpret<int>();
			
			for (var i = 0; i != length; i++)
			{
				if (intArray[i] == target)
					return true;
			}

			return false;
		}

		public static bool HasSerializer(this DynamicBuffer<ReplicatedEntitySerializer> buffer, uint target)
		{
			return HasSerializer(buffer, (int) target);
		}

		public static void AddSerializer(this DynamicBuffer<ReplicatedEntitySerializer> buffer, int target)
		{
			var length = buffer.Length;
			for (var i = 0; i != length; i++)
			{
				if (buffer[i].Index == target)
				{
					buffer.RemoveAt(i);
					length = buffer.Length;
					i--;
				}
			}

			buffer.Add(new ReplicatedEntitySerializer {Index = target});
		}
	}
}
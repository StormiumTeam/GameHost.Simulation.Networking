using Unity.Entities;

namespace Unity.NetCode
{
	public static class ReplicatedEntitySerializerExtensions
	{
		public static bool HasSerializer(this DynamicBuffer<ReplicatedEntitySerializer> buffer, int target)
		{
			var length = buffer.Length;
			for (var i = 0; i != length; i++)
			{
				if (buffer[i].Index == target)
					return true;
			}

			return false;
		}

		public static bool HasSerializer(this DynamicBuffer<ReplicatedEntitySerializer> buffer, uint target)
		{
			var length = buffer.Length;
			for (var i = 0; i != length; i++)
			{
				if (buffer[i].Index == target)
					return true;
			}

			return false;
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
using Unity.Entities;
using Unity.Networking.Transport;

namespace Unity.NetCode
{
	public interface IRpcCommand
	{
		void Execute(Entity               connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex);
		void Serialize(DataStreamWriter   writer);
		void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx);
	}
}
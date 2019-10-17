using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
	public struct IncomingPacket : IComponentData
	{
		public Entity Entity { get; set; }
		public NetworkConnection Connection { get; set; }
	}

	public struct IncomingSnapshotStreamBufferComponent : IBufferElementData
	{
		public byte Value;
	}
}
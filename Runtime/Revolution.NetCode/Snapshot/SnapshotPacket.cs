using Unity.Entities;

namespace Revolution.NetCode
{
	public struct SnapshotPacketTag : IComponentData
	{
	}

	public struct SnapshotPacketHolder : IBufferElementData
	{
		public Entity Value;
	}
}
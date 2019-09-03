using Unity.Entities;

namespace Revolution
{
	/// <summary>
	/// Hold the result of a snapshot generation
	/// </summary>
	public struct ClientSnapshotBuffer : IBufferElementData
	{
		public byte Data;
	}
}
using DefaultEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public interface ISnapshotSystem
	{
		void PrepareWrite(SnapshotFrameWriter writer, Entity storage);
	}
}
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	public partial class BroadcastInstigator
	{
		public class SnapshotState : SnapshotWriterState
		{
			public readonly int InstigatorId;

			public SnapshotState(int instigatorId)
			{
				InstigatorId = instigatorId;
			}

			public override bool Own(GameEntity local, SnapshotEntity remote)
			{
				return remote.Instigator == InstigatorId || created[local.Id] || base.Own(local, remote);
			}
		}
	}
}
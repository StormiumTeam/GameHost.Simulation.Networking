using Unity.Entities;

namespace Revolution
{
	public struct GhostSetup : ISetup
	{
		public ComponentDataFromEntity<GhostIdentifier> GhostIdentifierFromEntity;

		public void BeginSetup(JobComponentSystem system)
		{
			GhostIdentifierFromEntity = system.GetComponentDataFromEntity<GhostIdentifier>(true);
		}

		public uint this[Entity entity] => GhostIdentifierFromEntity.GetGhost(entity);
	}
}
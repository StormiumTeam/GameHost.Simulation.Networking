using Unity.Entities;
using Unity.NetCode;

namespace DefaultNamespace
{
	public static class GhostSystemStateComponentExtensions
	{
		public static uint GetGhostId(this ComponentDataFromEntity<GhostSystemStateComponent> ghostStateFromEntity, Entity target)
		{
			if (target == default || !ghostStateFromEntity.Exists(target))
				return 0;
			return (uint) ghostStateFromEntity[target].ghostId;
		}
	}
}
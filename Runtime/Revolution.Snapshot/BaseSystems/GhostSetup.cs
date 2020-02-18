using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace Revolution
{
	public struct GhostSetup : ISetup
	{
		public ComponentDataFromEntity<GhostIdentifier> GhostIdentifierFromEntity;

		public void BeginSetup(JobComponentSystem system
#if ENABLE_UNITY_COLLECTIONS_CHECKS
		                       , AtomicSafetyHandle safetyHandle
#endif
		)
		{
			GhostIdentifierFromEntity = system.GetComponentDataFromEntity<GhostIdentifier>(true);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyUtility.Replace(ref GhostIdentifierFromEntity, safetyHandle);
#endif
		}

		public uint this[Entity entity]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get { return GhostIdentifierFromEntity.GetGhost(entity); }
		}
	}
}
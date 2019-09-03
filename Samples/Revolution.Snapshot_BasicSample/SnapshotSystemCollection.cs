using System.Collections.Generic;
using Unity.Entities;

namespace DefaultNamespace
{
	public class SnapshotSystemCollection
	{
		public Dictionary<uint, object> Return(World world)
		{
			return new Dictionary<uint, object>
			{
				[0] = null,
				[1] = world.GetOrCreateSystem<HealthSnapshotSystem>(),
				[2] = world.GetOrCreateSystem<TranslationSnapshotSystem>(),
				[3] = world.GetOrCreateSystem<TestCustomArchetypeSerializer>()
			};
		}
	}
}
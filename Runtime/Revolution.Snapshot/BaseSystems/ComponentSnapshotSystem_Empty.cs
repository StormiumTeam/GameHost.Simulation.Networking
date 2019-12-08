using Unity.Collections;
using Unity.Entities;

namespace Revolution
{
	public abstract class ComponentSnapshotSystemEmpty<TTag> : EmptySnapshotSystem<ComponentSnapshotSystemEmpty<TTag>, TTag>
		where TTag : IComponentData
	{
		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp, NativeArrayOptions.UninitializedMemory)
			{
				[0] = typeof(TTag)
			};

		public struct SharedData
		{
		}
	}

	public struct ExcludeFromTagging : IComponentData
	{
	}

	public class ComponentSnapshotSystemTag<TTag> : ComponentSnapshotSystemEmpty<TTag>
		where TTag : IComponentData
	{
		public override ComponentType ExcludeComponent => typeof(ExcludeFromTagging);
	}
}
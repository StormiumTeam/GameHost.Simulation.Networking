using System;
using Karambolo.Common;
using Unity.Burst;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{
	public abstract class ComponentSnapshotSystem_Empty<TTag> : EmptySnapshotSystem<ComponentSnapshotSystem_Empty<TTag>, TTag>
		where TTag : IComponentData
	{
		public struct SharedData
		{
		}
	}
}
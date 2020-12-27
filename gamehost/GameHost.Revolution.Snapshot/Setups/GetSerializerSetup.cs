using GameHost.Core.Ecs;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Setups
{
	public struct GetSerializerSetup : ISnapshotSetupData
	{
		public SerializerBase SerializerBase { get; private set; }

		public GameWorld GameWorld { get; private set; }

		public void Create(IInstigatorSystem instigatorSystem)
		{
			SerializerBase = (SerializerBase) instigatorSystem;
			GameWorld      = (GameWorld) SerializerBase.DependencyResolver.DefaultStrategy.ResolveNow(typeof(GameWorld));
		}

		public void Begin(bool            isSerialization)
		{
			
		}

		public void Clean()
		{
			
		}
	}
}
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Setups
{
	public struct GetSerializerSetup : ISnapshotSetupData
	{
		public SerializerBase SerializerBase { get; private set; }

		public GameWorld GameWorld { get; private set; }

		public void Create(SerializerBase serializer)
		{
			SerializerBase = serializer;
			GameWorld      = (GameWorld) serializer.DependencyResolver.DefaultStrategy.ResolveNow(typeof(GameWorld));
		}

		public void Begin(bool            isSerialization)
		{
			
		}

		public void Clean()
		{
			
		}
	}
}
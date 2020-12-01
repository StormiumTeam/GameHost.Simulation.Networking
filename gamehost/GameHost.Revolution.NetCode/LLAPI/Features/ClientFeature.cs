using DefaultEcs;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using JetBrains.Annotations;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public class ClientFeature : MultiplayerFeature
	{
		public ClientFeature([NotNull] TransportDriver driver, TransportChannel reliable) : base(driver, reliable)
		{
		}
	}

	public class AddComponentsClientFeature : AppSystemWithFeature<ClientFeature>
	{
		public AddComponentsClientFeature(WorldCollection collection) : base(collection)
		{
		}

		protected override void OnFeatureAdded(Entity entity, ClientFeature obj)
		{
			entity.Set(new BroadcastInstigator(entity, 0, Context));
		}

		protected override void OnFeatureRemoved(Entity entity, ClientFeature obj)
		{
			if (entity.TryGet(out BroadcastInstigator broadcastInstigator))
				broadcastInstigator.Dispose();
		}
	}
}
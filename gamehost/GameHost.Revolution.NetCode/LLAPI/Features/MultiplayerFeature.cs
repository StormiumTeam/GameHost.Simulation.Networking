using GameHost.Applications;
using GameHost.Core.IO;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	public abstract class MultiplayerFeature : IFeature
	{
		public TransportChannel ReliableChannel { get; }

		public TransportDriver Driver { get; }

		protected MultiplayerFeature(TransportDriver driver, TransportChannel reliable)
		{
			Driver          = driver;
			ReliableChannel = reliable;
		}
	}
}
using System;

namespace GameHost.Revolution.NetCode.LLAPI.Components
{
	public struct ClientNetworkStats
	{
		public TimeSpan RawTick;
		public TimeSpan InterpolatedTick;
		public TimeSpan PredictedTick;

		public float Rtt;
	}
}
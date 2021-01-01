using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.NetCode.Components
{
	public struct LocalInstigatorId : IComponentData
	{
		public int Value;

		public LocalInstigatorId(int value) => Value = value;
	}
}
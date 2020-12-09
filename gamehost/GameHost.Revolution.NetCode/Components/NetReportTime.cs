using System.Diagnostics;
using GameHost.Core.Ecs;
using GameHost.Simulation.Application;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.Interfaces;
using GameHost.Simulation.Utility.EntityQuery;
using GameHost.Simulation.Utility.InterTick;
using GameHost.Simulation.Utility.Time;

namespace GameHost.Revolution.NetCode.Components
{
	public struct NetReportTime : IComponentData
	{
		public GameTime Begin, End;

		public RangeTick FrameRange => new((uint) (Begin.Frame == 0 ? End.Frame : Begin.Frame), (uint) End.Frame);

		/// <summary>
		/// If continuous is at 0, return <see cref="FrameRange"/>
		/// </summary>
		public RangeTick Active => Continuous == 0 ? FrameRange : default;

		/// <summary>
		/// How much of the same last report has been made?
		/// </summary>
		public uint Continuous;
	}

	[RestrictToApplication(typeof(SimulationApplication))]
	public class NetReportTimeSystem : AppSystem
	{
		private GameWorld gameWorld;

		public NetReportTimeSystem(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref gameWorld);
		}

		public NetReportTime Get(GameEntityHandle entity, out bool fromEntity)
		{
			Debug.Assert(DependencyResolver.Dependencies.Count == 0, "DependencyResolver.Dependencies.Count == 0");

			if (gameWorld.HasComponent<NetReportTime>(entity))
			{
				fromEntity = true;
				return gameWorld.GetComponentData<NetReportTime>(entity);
			}

			fromEntity = false;

			gameWorld.TryGetSingleton(out GameTime gameTime);
			return new NetReportTime {Begin = gameTime, End = gameTime};
		}
	}
}
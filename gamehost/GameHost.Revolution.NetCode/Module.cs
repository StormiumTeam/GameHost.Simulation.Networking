using System;
using System.Collections.Generic;
using DefaultEcs;
using GameHost.Applications;
using GameHost.Core.Ecs;
using GameHost.Core.Modules;
using GameHost.Injection;
using GameHost.Revolution.NetCode;
using GameHost.Simulation.Application;
using GameHost.Threading.Apps;
using GameHost.Worlds;

[assembly: RegisterAvailableModule("Revolution.NetCode", "guerro", typeof(Module))]
[assembly: AllowAppSystemResolving]

namespace GameHost.Revolution.NetCode
{
	public class Module : GameHostModule
	{
		public Module(Entity source, Context ctxParent, GameHostModuleDescription description) : base(source, ctxParent, description)
		{
			var global = new ContextBindingStrategy(Ctx, true).Resolve<GlobalWorld>();
			foreach (var application in global.World.Get<IApplication>())
			{
				if (application is CommonApplicationThreadListener app)
				{
					app.Schedule(() =>
					{
						var systems = new List<Type>();
						AppSystemResolver.ResolveFor<SimulationApplication>(typeof(Module).Assembly, systems);
						foreach (var system in systems)
							app.Data.Collection.GetOrCreate(system);
					}, default);
				}
			}
		}
	}
}
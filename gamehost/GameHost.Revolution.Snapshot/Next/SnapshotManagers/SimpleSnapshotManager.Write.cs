using System;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next.Data
{
	public partial class SimpleSnapshotManager
	{
		static class Write
		{
			public static void archetypes_data(BitBuffer buffer, SnapshotFrame frame)
			{
				var archetypeSpan = frame.Root.Archetypes;

				var previousArchetypeId = 0u;
				var previousSystemId    = 0u;

				buffer.AddUIntD4((uint) archetypeSpan.Length);
				foreach (var archetype in archetypeSpan)
				{
					previousSystemId = 0u;

					var systems = frame.Root.GetArchetypeSystems(archetype);
					buffer.AddUIntD4Delta(archetype, previousArchetypeId)
					      .AddUIntD4((uint) systems.Length);
					foreach (var system in systems)
					{
						buffer.AddUIntD4Delta(system, previousSystemId);
						previousSystemId = system;
					}

					previousArchetypeId = archetype;
				}
			}

			public static void entities(BitBuffer buffer, SnapshotFrame frame)
			{
				var entitySpan    = frame.Entities;
				var archetypeSpan = frame.EntityToArchetype;

				GameEntity prevLocal     = default;
				uint       prevArchetype = default;

				buffer.AddUIntD4((uint) entitySpan.Length);
				for (var i = 0; i < entitySpan.Length; i++)
				{
					buffer.AddUIntD4Delta(entitySpan[i].Id, prevLocal.Id)
					      .AddUIntD4Delta(entitySpan[i].Version, prevLocal.Version)
					      .AddUIntD4Delta(archetypeSpan[(int) entitySpan[i].Id], prevArchetype);

					prevLocal     = entitySpan[i];
					prevArchetype = archetypeSpan[(int)entitySpan[i].Id];

					Console.WriteLine($"{prevLocal} {prevArchetype}");
				}
			}
		}
	}
}
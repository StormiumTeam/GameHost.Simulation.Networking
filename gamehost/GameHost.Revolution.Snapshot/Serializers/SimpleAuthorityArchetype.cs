using System;
using System.Collections.Generic;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public class SimpleAuthorityArchetype : IAuthorityArchetype
	{
		public readonly GameWorld GameWorld;
		
		public readonly ComponentType RemoteAuthority;
		public readonly ComponentType LocalAuthority;

		public SimpleAuthorityArchetype(GameWorld gameWorld, ComponentType remoteAuthority, ComponentType localAuthority)
		{
			GameWorld = gameWorld;

			RemoteAuthority = remoteAuthority;
			LocalAuthority  = localAuthority;
		}

		public bool IsArchetypeValid(in EntityArchetype archetype)
		{
			var components = GameWorld.Boards.Archetype.GetComponentTypes(archetype.Id);
			foreach (var comp in components)
				if (comp == RemoteAuthority.Id)
				{
					return true;
				}

			return false;
		}

		public void TryKeepAuthority(GameEntity entity, bool enable, HashSet<ComponentType> kept)
		{
			switch (enable)
			{
				case true:
					if (!GameWorld.HasComponent(entity.Handle, LocalAuthority))
						GameWorld.AddComponent(entity.Handle, LocalAuthority);
					
					Console.WriteLine($"    Add {GameWorld.Boards.ComponentType.NameColumns[(int) LocalAuthority.Id]}");
					
					kept.Add(LocalAuthority);
					break;
				case false:
					if (kept.Contains(LocalAuthority))
						return;
					
					Console.WriteLine($"    Remove {GameWorld.Boards.ComponentType.NameColumns[(int) LocalAuthority.Id]}");
					
					GameWorld.RemoveComponent(entity.Handle, LocalAuthority);
					break;
			}
		}
	}
}
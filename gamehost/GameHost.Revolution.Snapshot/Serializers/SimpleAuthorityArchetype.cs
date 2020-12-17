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
		public readonly ComponentType None;

		public SimpleAuthorityArchetype(GameWorld gameWorld, ComponentType remoteAuthority, ComponentType localAuthority, ComponentType none)
		{
			GameWorld = gameWorld;

			RemoteAuthority = remoteAuthority;
			LocalAuthority  = localAuthority;
			None            = none;
		}

		public bool IsArchetypeValid(in EntityArchetype archetype)
		{
			var components = GameWorld.Boards.Archetype.GetComponentTypes(archetype.Id);
			var contains   = false;
			foreach (var comp in components)
			{
				if (comp == RemoteAuthority.Id)
					contains = true;
				else if (comp == None.Id)
				{
					Console.WriteLine("nope!");
					return false;
				}
			}

			return contains;
		}

		public void TryKeepAuthority(GameEntity entity, bool enable, HashSet<ComponentType> kept)
		{
			switch (enable)
			{
				case true:
					if (!GameWorld.HasComponent(entity.Handle, LocalAuthority))
						GameWorld.AddComponent(entity.Handle, LocalAuthority);
					
					kept.Add(LocalAuthority);
					break;
				case false:
					if (kept.Contains(LocalAuthority))
						return;
					
					GameWorld.RemoveComponent(entity.Handle, LocalAuthority);
					break;
			}
		}
	}
}
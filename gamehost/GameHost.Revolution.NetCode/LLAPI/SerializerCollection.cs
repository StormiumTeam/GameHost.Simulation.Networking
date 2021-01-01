using System;
using System.Collections.Generic;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Simulation.Application;
using StormiumTeam.GameBase.Utility.Misc;

namespace GameHost.Revolution.NetCode.LLAPI
{
	[RestrictToApplication(typeof(SimulationApplication))]
	public class SerializerCollection : AppSystem
	{
		private Dictionary<string, (Type type, Func<ISnapshotInstigator, IInstigatorSystem> createFunc)> map = new();

		public SerializerCollection(WorldCollection collection) : base(collection)
		{
		}

		public event Action<IReadOnlyDictionary<string, (Type type, Func<ISnapshotInstigator, IInstigatorSystem> createFunc)>> OnCollectionUpdate;

		public void Register(Type type, Func<ISnapshotInstigator, IInstigatorSystem> createFunc)
		{
			map[IInstigatorSystem.GetIdentifier(type)] = (type, createFunc);

			OnCollectionUpdate?.Invoke(map);
		}

		public void Register<TSerializer>(Func<ISnapshotInstigator, TSerializer> createFunc)
			where TSerializer : IInstigatorSystem
		{
			Register(typeof(TSerializer), instigator => createFunc(instigator));
		}

		public bool TryGet(string name, out (Type type, Func<ISnapshotInstigator, IInstigatorSystem> create) output)
		{
			if (map.TryGetValue(name, out var args))
			{
				output = (args.type, args.createFunc);
				return true;
			}

			output = default;
			return false;
		}
	}
}
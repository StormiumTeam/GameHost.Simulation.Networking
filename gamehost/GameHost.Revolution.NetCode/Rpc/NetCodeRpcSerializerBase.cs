using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Rpc
{
	public abstract class NetCodeRpcSerializerBase : AppObject, INetCodeRpcSerializer
	{
		protected GameWorld GameWorld;
		
		protected NetCodeRpcSerializerBase(Context context) : base(context)
		{
			DependencyResolver.Add(() => ref GameWorld);
			DependencyResolver.OnComplete(OnDependenciesResolved);
		}

		protected virtual void OnDependenciesResolved(IEnumerable<object> deps)
		{
		}

		public ISnapshotInstigator      Instigator { get; set; }
		public InstigatorSystem System     { get; set; }
		
		public abstract bool                     CanSerialize(Type type);

		public abstract void Serialize(in BitBuffer bitBuffer, Type type, Span<byte> data);

		public abstract void Deserialize(in BitBuffer bitBuffer);
	}
	
	public abstract class NetCodeRpcSerializerBase<T, TSetup> : NetCodeRpcSerializerBase
		where T : struct
		where TSetup : struct, ISnapshotSetupData
	{
		private   TSetup    setup;

		public NetCodeRpcSerializerBase(Context context) : base(context)
		{
			setup = new TSetup();
		}

		protected override void OnDependenciesResolved(IEnumerable<object> deps)
		{
			setup.Create(this);
		}

		public InstigatorSystem System { get; set; }

		public sealed override bool CanSerialize(Type type)
		{
			return type == typeof(T);
		}

		public sealed override void Serialize(in BitBuffer bitBuffer, Type type, Span<byte> data)
		{
			setup.Begin(true);
			OnSerialize(bitBuffer, MemoryMarshal.Read<T>(data), setup);
		}

		protected abstract void OnSerialize(in   BitBuffer bitBuffer, in T data, TSetup setup);
		protected abstract void OnDeserialize(in BitBuffer bitBuffer, TSetup setup);

		public sealed override void Deserialize(in BitBuffer bitBuffer)
		{
			setup.Begin(false);
			OnDeserialize(bitBuffer, setup);
		}
	}
}
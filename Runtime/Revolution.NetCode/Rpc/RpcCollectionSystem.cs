using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution.NetCode
{
	[UpdateInGroup(typeof(ClientAndServerInitializationSystemGroup))]
	public class RpcCollectionSystem : ComponentSystem
	{
		public Dictionary<uint, RpcProcessSystemBase> SystemProcessors;

		protected override void OnCreate()
		{
			base.OnCreate();

			SystemProcessors = new Dictionary<uint, RpcProcessSystemBase>();
		}

		protected override void OnUpdate()
		{

		}

		public void SetFixedCollection(Action<World, CollectionBuilder<RpcProcessSystemBase>> build)
		{
			var cb = new CollectionBuilder<RpcProcessSystemBase>();
			cb.Set(0, World.GetOrCreateSystem<DefaultRpcProcessSystem<RpcSetNetworkId>>());
			build(World, cb);
			SystemProcessors = cb.Build();
			foreach (var processor in SystemProcessors)
			{
				processor.Value.SystemRpcId = processor.Key;
			}
		}
	}

	public class RpcProgressSystemGroup : ComponentSystemGroup
	{
		protected override void OnUpdate()
		{
		}
	}

	public abstract class RpcProcessSystemBase : JobComponentSystem
	{
		public Entity RpcTarget   { get; private set; }
		public uint   SystemRpcId { get; internal set; }

		public abstract void Prepare();

		protected override JobHandle OnUpdate(JobHandle i) => i;

		public void BeginDeserialize(Entity target, uint systemId)
		{
			if (SystemRpcId > 0 && SystemRpcId != systemId)
				throw new InvalidOperationException("A system id shouldn't be changed in runtime.");

			RpcTarget   = target;
			SystemRpcId = systemId;
		}

		public abstract void ProcessReceive(DataStreamReader reader, ref DataStreamReader.Context ctx);
	}

	public abstract class RpcProcessSystemBase<TRpc> : RpcProcessSystemBase
		where TRpc : struct, IRpcCommand
	{
		public override void Prepare()
		{
		}

		public override void ProcessReceive(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var data = default(TRpc);
			data.ReadFrom(reader, ref ctx);

			ExecuteCall(data);
		}

		public abstract void ExecuteCall(TRpc rpc);

		public virtual RpcQueue<TRpc> RpcQueue
		{
			get
			{
				Prepare();
				return new RpcQueue<TRpc> {RpcType = (int) SystemRpcId};
			}
		}
	}

	public class DefaultRpcProcessSystem<TRpc> : RpcProcessSystemBase<TRpc>
		where TRpc : struct, IRpcCommand
	{
		public override void ExecuteCall(TRpc rpc)
		{
			rpc.Execute(RpcTarget, World);
		}
	}
}
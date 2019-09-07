using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
	[UpdateInGroup(typeof(ClientAndServerInitializationSystemGroup))]
	public class RpcCollectionSystem : ComponentSystem
	{
		public Dictionary<uint, RpcProcessSystemBase> SystemProcessors;

		internal RpcQueue<RpcSetNetworkId> NetworkIdRpcQueue =>
			new RpcQueue<RpcSetNetworkId>
			{
				RpcType = 1
			};

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
			cb.Set(1, World.GetOrCreateSystem<DefaultRpcProcessSystem<RpcSetNetworkId>>());
			SystemProcessors = cb.Build();
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
		public uint   SystemRpcId { get; private set; }

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
		where TRpc : struct, IRpcCommand<TRpc>
	{
		private EntityQuery m_EntityWithoutBufferQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_EntityWithoutBufferQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection)},
				None = new ComponentType[] {typeof(TRpc)}
			});
		}

		public override void Prepare()
		{
			if (m_EntityWithoutBufferQuery.IsEmptyIgnoreFilter)
				return;

			using (var entities = m_EntityWithoutBufferQuery.ToEntityArray(Allocator.TempJob))
			{
				foreach (var entity in entities)
				{
					var buffer = EntityManager.AddBuffer<TRpc>(entity);
					buffer.ResizeUninitialized(buffer.Capacity + 1);
					buffer.Clear();
				}
			}
		}

		public override void ProcessReceive(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var data = default(TRpc);
			data.ReadFrom(reader, ref ctx);

			ExecuteCall(data);
		}

		public abstract void ExecuteCall(TRpc rpc);

		public virtual RpcQueue<TRpc> GetQueue()
		{
			Prepare();
			return new RpcQueue<TRpc> {RpcType = (int) SystemRpcId};
		}
	}

	public class DefaultRpcProcessSystem<TRpc> : RpcProcessSystemBase<TRpc>
		where TRpc : struct, IRpcCommand<TRpc>
	{
		public override void ExecuteCall(TRpc rpc)
		{
			rpc.Execute(RpcTarget, World);
		}
	}
}
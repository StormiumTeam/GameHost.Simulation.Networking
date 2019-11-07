using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
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
			cb.Set(0, World.GetOrCreateSystem<RpcCommandRequest<RpcSetNetworkId>>());
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
		public abstract void ProcessSend(NativeArray<Entity> entities);
	}

	public class RpcCommandRequest<TRpc> : RpcProcessSystemBase
		where TRpc : struct, IRpcCommandRequestComponentData
	{
		public RpcQueue<TRpc> Queue
		{
			get
			{
				if (SystemRpcId == 0 && typeof(TRpc) != typeof(RpcSetNetworkId))
					throw new InvalidOperationException();

				return new RpcQueue<TRpc> {RpcType = (int) SystemRpcId};
			}
		}

		private bool m_SupportExecuteInterface;
		private EntityArchetype m_ReceivedRequestArchetype;
		private EntityQuery     m_RpcSendQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_ReceivedRequestArchetype = EntityManager.CreateArchetype(typeof(TRpc));
			m_RpcSendQuery             = GetEntityQuery(typeof(TRpc), typeof(SendRpcCommandRequestComponent));

			m_SupportExecuteInterface = typeof(TRpc).GetInterfaces().Contains(typeof(IRpcCommandRequestExecuteNow));
		}

		public override void Prepare()
		{
		}

		public override void ProcessReceive(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			Debug.Log($"receiving from {RpcTarget} -> {typeof(TRpc)}");
			
			var data = default(TRpc);
			data.SourceConnection = RpcTarget;
			data.Deserialize(reader, ref ctx);

			if (m_SupportExecuteInterface && data is IRpcCommandRequestExecuteNow asExecuteNow)
			{
				asExecuteNow.Execute(EntityManager);
			}
			else
			{
				var reqEnt = EntityManager.CreateEntity(m_ReceivedRequestArchetype);
				EntityManager.SetComponentData(reqEnt, data);
			}
		}

		private unsafe void AddToBuffer(Entity connection, ref DataStreamWriter writer)
		{
		Debug.Log($"sending to {connection} --> {typeof(TRpc)}");
		
			var outgoingData = EntityManager.GetBuffer<OutgoingRpcDataStreamBufferComponent>(connection).Reinterpret<byte>();
			var data = new NativeArray<byte>(writer.Length, Allocator.Temp);
			
			writer.CopyTo(0, data.Length, data);
			outgoingData.AddRange(data);
		}

		public override void ProcessSend(NativeArray<Entity> connectionEntities)
		{
			if (m_RpcSendQuery.CalculateEntityCount() == 0)
				return;
			
			var tmp = new DataStreamWriter(1024, Allocator.TempJob);
			tmp.Write(SystemRpcId);

			var rpcData = m_RpcSendQuery.ToComponentDataArray<TRpc>(Allocator.TempJob);
			var target  = m_RpcSendQuery.ToComponentDataArray<SendRpcCommandRequestComponent>(Allocator.TempJob);
			var length  = rpcData.Length;
			for (var i = 0; i != length; i++)
			{
				rpcData[i].Serialize(tmp);

				// single target
				if (target[i].TargetConnection != default)
				{
					AddToBuffer(target[i].TargetConnection, ref tmp);
				}
				// all target
				else
				{
					foreach (var connectionEnt in connectionEntities)
					{
						AddToBuffer(connectionEnt, ref tmp);
					}
				}

				tmp.Clear();
			}

			rpcData.Dispose();
			target.Dispose();

			EntityManager.DestroyEntity(m_RpcSendQuery);

			tmp.Dispose();
		}
	}
}
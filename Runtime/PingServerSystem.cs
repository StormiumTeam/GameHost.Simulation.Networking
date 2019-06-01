using package.stormiumteam.networking.runtime.Rpc;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace DefaultNamespace
{
	public struct RpcPing : IRpcBase<RpcPing>
	{
		public RpcBase.Header Header { get; set; }

		public void SetupHeader(out rpc_Serialize<RpcPing> serialize, out rpc_Deserialize<RpcPing> deserialize)
		{
			serialize   = (ref RpcPing p, DataStreamWriter d) => { };
			deserialize = (ref RpcPing p, DataStreamReader d, ref DataStreamReader.Context c) => { };
		}
	}

	[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
	public class PingServerSystem : JobComponentSystem
	{
		/*private struct PingJob : IJobForEachWithEntity<>
		{
			
		}

		private NativeArray<float> m_NextPing;
		private EntityQuery m_ConnectionQuery;
		private RpcBaseQueue<RpcPing> m_RpcQueue;

		protected override void OnCreate()
		{
			base.OnCreate();
			
			m_NextPing = new NativeArray<float>(1, Allocator.Persistent);
			m_RpcQueue = World.GetOrCreateSystem<RpcSystem>().GetRpcBaseQueue<RpcPing>();
			m_ConnectionQuery = GetEntityQuery(typeof(NetworkStreamConnection));
		}

		protected override void OnDestroy()
		{
			base.OnDestroy();
			
			m_NextPing.Dispose();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			if (m_ConnectionQuery.IsEmptyIgnoreFilter)
				return inputDeps;
			
			return new PingJob
			{
				Delay = m_NextPing,
				Dt = Time.deltaTime,
				OutgoingRpcData = GetBufferFromEntity<OutgoingRpcDataStreamBufferComponent>(),
				RpcQueue = m_RpcQueue
			}.Schedule(inputDeps);
		}*/
		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return inputDeps;
		}
	}
}
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution.NetCode
{
	[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
	public unsafe class SnapshotSendSystem : ComponentSystem
	{
		private Dictionary<Entity, SerializeClientData> m_SerializeLookup;

		private EntityQuery m_ConnectionGroup;
		private EntityQuery m_ConnectionWithoutSnapshotBufferGroup;

		private ServerSimulationSystemGroup m_ServerSimulationSystemGroup;
		private NetworkStreamReceiveSystem  m_ReceiveSystem;
		private CreateSnapshotSystem        m_CreateSnapshotSystem;

		protected override void OnCreate()
		{
			base.OnCreate();
			m_SerializeLookup = new Dictionary<Entity, SerializeClientData>(32);
			m_ConnectionGroup = GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkSnapshotAckComponent), typeof(NetworkStreamInGame)}
			});
			m_ConnectionWithoutSnapshotBufferGroup = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkStreamInGame)},
				None = new ComponentType[] {typeof(ClientSnapshotBuffer)}
			});
			m_ServerSimulationSystemGroup = World.GetOrCreateSystem<ServerSimulationSystemGroup>();
			m_ReceiveSystem               = World.GetOrCreateSystem<NetworkStreamReceiveSystem>();
			m_CreateSnapshotSystem        = World.GetOrCreateSystem<CreateSnapshotSystem>();
		}

		protected override void OnUpdate()
		{
			using (var entities = m_ConnectionWithoutSnapshotBufferGroup.ToEntityArray(Allocator.TempJob))
			{
				foreach (var entity in entities)
				{
					var buffer = EntityManager.AddBuffer<ClientSnapshotBuffer>(entity);
					buffer.ResizeUninitialized(1200);
					buffer.Clear();
				}
			}

			var deleteKeys = new NativeList<Entity>(8, Allocator.Temp);
			foreach (var kvp in m_SerializeLookup)
			{
				if (!EntityManager.Exists(kvp.Key))
					deleteKeys.Add(kvp.Key);
			}

			foreach (var key in deleteKeys)
			{
				m_SerializeLookup.Remove(key);
			}

			var connectionEntities           = m_ConnectionGroup.ToEntityArray(Allocator.TempJob);
			var networkStreamConnectionArray = m_ConnectionGroup.ToComponentDataArray<NetworkStreamConnection>(Allocator.TempJob);
			var ackComponentArray            = m_ConnectionGroup.ToComponentDataArray<NetworkSnapshotAckComponent>(Allocator.TempJob);
			foreach (var entity in connectionEntities)
			{
				if (m_SerializeLookup.ContainsKey(entity))
					continue;
				m_SerializeLookup[entity] = new SerializeClientData(Allocator.Persistent);
			}

			m_CreateSnapshotSystem.CreateSnapshot(m_ServerSimulationSystemGroup.ServerTick, m_SerializeLookup);

			var pipeline  = m_ReceiveSystem.SnapshotPipeline;
			var driver    = m_ReceiveSystem.Driver;
			var localTime = NetworkTimeSystem.TimestampMS;
			for (var ent = 0; ent < connectionEntities.Length; ent++)
			{
				var entity     = connectionEntities[ent];
				var connection = networkStreamConnectionArray[ent];
				var ack        = ackComponentArray[ent];

				var buffer = EntityManager.GetBuffer<ClientSnapshotBuffer>(entity);
				var writer = new DataStreamWriter(buffer.Length + 64, Allocator.TempJob);
				writer.Write((byte) NetworkStreamProtocol.Snapshot);
				writer.Write(localTime);
				writer.Write(ack.LastReceivedRemoteTime - (localTime - ack.LastReceiveTimestamp));
				writer.Write(m_ServerSimulationSystemGroup.ServerTick);
				
				Debug.Log($"Send at {m_ServerSimulationSystemGroup.ServerTick}");
				
				writer.WriteBytes((byte*) buffer.GetUnsafePtr(), buffer.Length);

				driver.Send(pipeline, connection.Value, writer);
				writer.Dispose();
			}

			connectionEntities.Dispose();
			networkStreamConnectionArray.Dispose();
			ackComponentArray.Dispose();
		}

		protected override void OnDestroy()
		{
			foreach (var kvp in m_SerializeLookup)
			{
				kvp.Value.Dispose();
			}

			m_SerializeLookup.Clear();
		}
	}
}
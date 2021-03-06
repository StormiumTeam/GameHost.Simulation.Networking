using System.Collections.Generic;
using K4os.Compression.LZ4;
using Revolution;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Profiling;

namespace Unity.NetCode
{
	[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
	[UpdateAfter(typeof(TransformSystemGroup))]
	[AlwaysUpdateSystem]
	public unsafe class SnapshotSendSystem : ComponentSystem
	{
		private Dictionary<Entity, ReferencableSerializeClientData> m_SerializeLookup;

		private EntityQuery m_ConnectionGroup;
		private EntityQuery m_ConnectionWithoutSnapshotBufferGroup;

		private ServerSimulationSystemGroup m_ServerSimulationSystemGroup;
		private NetworkStreamReceiveSystem  m_ReceiveSystem;
		private CreateSnapshotSystem        m_CreateSnapshotSystem;

		private DataStreamWriter m_DataStream;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_SerializeLookup = new Dictionary<Entity, ReferencableSerializeClientData>(32);
			m_ConnectionGroup = GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[] {typeof(NetworkStreamConnection), typeof(NetworkSnapshotAckComponent), /*typeof(NetworkStreamInGame)*/}
			});
			m_ConnectionWithoutSnapshotBufferGroup = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(NetworkStreamConnection), /* typeof(NetworkStreamInGame)*/},
				None = new ComponentType[] {typeof(ClientSnapshotBuffer)}
			});
			m_ServerSimulationSystemGroup = World.GetOrCreateSystem<ServerSimulationSystemGroup>();
			m_ReceiveSystem               = World.GetOrCreateSystem<NetworkStreamReceiveSystem>();
			m_CreateSnapshotSystem        = World.GetOrCreateSystem<CreateSnapshotSystem>();

			m_DataStream = new DataStreamWriter(32_768, Allocator.Persistent);
		}

		protected override void OnUpdate()
		{
			if (!m_ConnectionWithoutSnapshotBufferGroup.IsEmptyIgnoreFilter)
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
			}

			using (var deleteKeys = new NativeList<Entity>(8, Allocator.Temp))
			{
				foreach (var kvp in m_SerializeLookup)
				{
					if (!EntityManager.Exists(kvp.Key))
						deleteKeys.Add(kvp.Key);
				}

				foreach (var key in deleteKeys)
				{
					m_SerializeLookup[key].Data.Dispose();
					m_SerializeLookup.Remove(key);
				}
			}

			var connectionEntities           = m_ConnectionGroup.ToEntityArray(Allocator.TempJob);
			var networkStreamConnectionArray = m_ConnectionGroup.ToComponentDataArray<NetworkStreamConnection>(Allocator.TempJob);
			var ackComponentArray            = m_ConnectionGroup.ToComponentDataArray<NetworkSnapshotAckComponent>(Allocator.TempJob);
			foreach (var entity in connectionEntities)
			{
				if (m_SerializeLookup.ContainsKey(entity))
					continue;

				m_SerializeLookup[entity] = new ReferencableSerializeClientData
				{
					Data = new SerializeClientData(Allocator.Persistent)
				};
			}

			m_CreateSnapshotSystem.CreateSnapshot(m_ServerSimulationSystemGroup.ServerTick, m_SerializeLookup);

			var localTime = NetworkTimeSystem.TimestampMS;
			for (var ent = 0; ent < connectionEntities.Length; ent++)
			{
				var entity     = connectionEntities[ent];
				var connection = networkStreamConnectionArray[ent];
				var ack        = ackComponentArray[ent];

				var buffer = EntityManager.GetBuffer<ClientSnapshotBuffer>(entity);

				m_DataStream.Clear();
				m_DataStream.Write((byte) NetworkStreamProtocol.Snapshot);
				m_DataStream.Write(localTime);
				var returnTime = ack.LastReceivedRemoteTime;
				if (returnTime != 0)
					returnTime -= (localTime - ack.LastReceiveTimestamp);
				m_DataStream.Write(returnTime);
				m_DataStream.Write(ack.ServerCommandAge);
				m_DataStream.Write(byte.MaxValue);
				m_DataStream.Write(m_ServerSimulationSystemGroup.ServerTick);

				Profiler.BeginSample("Compressing");
				var compressed       = UnsafeUtility.Malloc(LZ4Codec.MaximumOutputSize(buffer.Length), UnsafeUtility.AlignOf<byte>(), Allocator.Temp);
				var compressedLength = LZ4Codec.MaximumOutputSize(buffer.Length);
				{
					var encoder = LZ4Level.L04_HC; // default encoder
					//encoder = LZ4Level.L12_MAX;

					var size = LZ4Codec.Encode((byte*) buffer.GetUnsafePtr(), buffer.Length, (byte*) compressed, compressedLength, encoder);
					Profiler.EndSample();
					m_DataStream.Write(size);
					m_DataStream.Write(buffer.Length);
					
					if (size > 1000)
						Debug.Log($"s={size} b={buffer.Length}");

					m_DataStream.WriteBytes((byte*) compressed, size);
				}
				UnsafeUtility.Free(compressed, Allocator.Temp);

				m_ReceiveSystem.Driver.Send(m_ReceiveSystem.ReliablePipeline, connection.Value, m_DataStream);
			}

			connectionEntities.Dispose();
			networkStreamConnectionArray.Dispose();
			ackComponentArray.Dispose();
		}

		protected override void OnDestroy()
		{
			m_DataStream.Dispose();

			foreach (var kvp in m_SerializeLookup)
			{
				kvp.Value.Data.Dispose();
			}

			m_SerializeLookup.Clear();
		}
	}
}
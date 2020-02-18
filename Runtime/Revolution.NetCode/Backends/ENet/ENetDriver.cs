using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace ENet
{
	public unsafe struct ENetDriver : INetworkDriver
	{
		private struct DriverEvent
		{
			public NetworkEvent.Type Type;
			public int               StreamOffset;
			public int               Length;
		}

		private struct Connection : IDisposable
		{
			private IntPtr m_PeerPtr;

			public Peer Peer
			{
				get { return new Peer(m_PeerPtr); }
				set
				{
					if (value.ID != Id)
						throw new InvalidOperationException("Can't set a peer with a different 'Id'");
					m_PeerPtr = value.NativeData;
				}
			}

			public uint Id;

			private UnsafeList* m_IncomingEvents;
			private UnsafeList* m_DataStream;

			public int IncomingEventCount => m_IncomingEvents->Length;


			public static Connection Create(Peer peer)
			{
				Connection connection;
				connection.m_PeerPtr = peer.NativeData;
				connection.Id        = peer.ID;
				connection.m_DataStream = UnsafeList.Create
				(
					UnsafeUtility.SizeOf<byte>(),
					UnsafeUtility.AlignOf<byte>(),
					4096,
					Allocator.Persistent,
					NativeArrayOptions.ClearMemory
				);
				connection.m_IncomingEvents = UnsafeList.Create
				(
					UnsafeUtility.SizeOf<DriverEvent>(),
					UnsafeUtility.AlignOf<DriverEvent>(),
					8,
					Allocator.Persistent,
					NativeArrayOptions.ClearMemory
				);
				return connection;
			}

			public void ResetDataStream()
			{
				// set capacity back to 8192
				m_DataStream->Resize<byte>(8192);
				m_DataStream->Length = 0;
			}

			public void AddEvent(NetworkEvent.Type type)
			{
				m_IncomingEvents->Add(new DriverEvent {Type = type});
			}

			public void AddMessage(IntPtr data, int length)
			{
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				if (data == IntPtr.Zero)
					throw new NullReferenceException();
				if (length < 0)
					throw new IndexOutOfRangeException(nameof(length) + " < 0");
#endif
				var prevLen = m_DataStream->Length;
				m_DataStream->AddRange<byte>(data.ToPointer(), length);
				m_IncomingEvents->Add(new DriverEvent {Type = NetworkEvent.Type.Data, StreamOffset = prevLen, Length = length});
			}

			public NetworkEvent.Type PopEvent(out DataStreamReader bs)
			{
				bs = default;
				if (m_IncomingEvents->Length == 0)
					return NetworkEvent.Type.Empty;

				var ev = UnsafeUtility.ReadArrayElement<DriverEvent>(m_IncomingEvents->Ptr, 0);
				if (m_IncomingEvents->Length > 0)
				{
					//m_IncomingEvents->RemoveAtSwapBack<DriverEvent>(0);
					m_IncomingEvents->Length--;
					UnsafeUtility.MemMove(m_IncomingEvents->Ptr, (byte*) m_IncomingEvents->Ptr + sizeof(DriverEvent), sizeof(DriverEvent) * m_IncomingEvents->Length);
				}

				if (ev.Type == NetworkEvent.Type.Data)
				{
					var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<byte>((byte*) m_DataStream->Ptr + ev.StreamOffset, sizeof(byte), ev.Length);
#if ENABLE_UNITY_COLLECTIONS_CHECKS
					NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, AtomicSafetyHandle.GetTempUnsafePtrSliceHandle());
#endif

					bs = new DataStreamReader(slice);
				}

				return ev.Type;
			}

			public void Dispose()
			{
				m_IncomingEvents->Dispose();
				m_DataStream->Dispose();
			}
		}

		private struct CleanJob : IJob
		{
			public ENetDriver driver;

			public void Execute()
			{
				var connections = driver.m_Connections.GetValueArray(Allocator.Temp);
				for (var i = 0; i != connections.Length; i++)
				{
					/*if (!driver.m_QueuedForDisconnection[i] && connections[i].IncomingEventCount > 0)
						throw new InvalidOperationException("A connection still had events in queue!");*/
					while (connections[i].PopEvent(out _) != NetworkEvent.Type.Empty)
					{
						
					}

					connections[i].ResetDataStream();
					if (driver.m_QueuedForDisconnection[i])
					{
						driver.RemoveConnection(connections[i].Id);
					}
				}
			}
		}

		private struct UpdateJob : IJob
		{
			public ENetDriver driver;

			public void Execute()
			{
				for (var i = 0; i != driver.m_PacketsToSend.Length; i++)
				{
					var packet = driver.m_PacketsToSend[i];
					var info   = driver.m_PacketsChannelToUse[i];

					info.Peer.Send(info.Channel, ref packet);
				}

				driver.m_PacketsToSend.Clear();
				driver.m_PacketsChannelToUse.Clear();

				Event netEvent;
				var   polled = false;
				while (!polled)
				{
					if (driver.m_Host.CheckEvents(out netEvent) <= 0)
					{
						if (driver.m_Host.Service(0, out netEvent) <= 0)
							break;

						polled = true;
					}

					var peerId = (int) netEvent.Peer.ID;
					if (!driver.m_Connections.TryGetValue(netEvent.Peer.ID, out var connection))
					{
						connection = driver.AddConnection(netEvent.Peer);
					}

					switch (netEvent.Type)
					{
						case NetEventType.None:
							break;
						case NetEventType.Connect:
							if (driver.Listening)
							{
								driver.m_QueuedConnections.Enqueue(netEvent.Peer.ID);
							}

							break;
						case NetEventType.Receive:
							connection.AddMessage(netEvent.Packet.Data, netEvent.Packet.Length);
							netEvent.Packet.Dispose();
							break;
						case NetEventType.Disconnect:
						case NetEventType.Timeout:
						{
							connection.AddEvent(NetworkEvent.Type.Disconnect);
							driver.m_QueuedForDisconnection[peerId] = true;

							// increment version
							var ver = driver.m_ConnectionVersions[peerId];
							ver++;
							driver.m_ConnectionVersions[peerId] = ver;
							break;
						}

						default:
							throw new ArgumentOutOfRangeException();
					}
				}
			}
		}

		/// <summary>
		/// The sockets that is currently used
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private Host m_Host;

		/// <summary>
		/// The bind address
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private Address m_Address;

		[NativeDisableParallelForRestriction] private NativeArray<int>  m_ConnectionVersions;
		[NativeDisableParallelForRestriction] private NativeArray<bool> m_QueuedForDisconnection;

		[NativeDisableParallelForRestriction] private NativeHashMap<uint, Connection> m_Connections;
		[NativeDisableParallelForRestriction] private NativeQueue<uint>               m_QueuedConnections;
		[NativeDisableParallelForRestriction] private NativeList<int>                 m_PipelineReliableIds;
		[NativeDisableParallelForRestriction] private NativeList<int>                 m_PipelineUnreliableIds;
		private                                       int                             m_PipelineCount;

		private struct SendPacket
		{
			public Peer Peer;
			public byte Channel;
		}

		[NativeDisableParallelForRestriction] private NativeList<Packet>     m_PacketsToSend;
		[NativeDisableParallelForRestriction] private NativeList<SendPacket> m_PacketsChannelToUse;

		public Host Host => m_Host;

		private uint m_MaxConnections;
		public  uint MaxConnections => m_MaxConnections;

		public ENetDriver(uint maxConnections)
		{
			m_MaxConnections = maxConnections;

			m_Address                = default;
			m_Host                   = new Host();
			m_PacketsToSend          = new NativeList<Packet>(32, Allocator.Persistent);
			m_PacketsChannelToUse    = new NativeList<SendPacket>(32, Allocator.Persistent);
			m_ConnectionVersions     = new NativeArray<int>((int) maxConnections, Allocator.Persistent);
			m_QueuedForDisconnection = new NativeArray<bool>((int) maxConnections, Allocator.Persistent);
			m_Connections            = new NativeHashMap<uint, Connection>(32, Allocator.Persistent);
			m_QueuedConnections      = new NativeQueue<uint>(Allocator.Persistent);
			m_PipelineReliableIds    = new NativeList<int>(Allocator.Persistent);
			m_PipelineUnreliableIds  = new NativeList<int>(Allocator.Persistent);
			m_PipelineCount          = 1;

			for (var i = 0; i != m_ConnectionVersions.Length; i++)
				m_ConnectionVersions[i] = 1;

			Listening = false;
			m_DidBind = false;
		}

		public void Dispose()
		{
			if (IsCreated)
			{
				var connections = m_Connections.GetValueArray(Allocator.Temp);
				foreach (var con in connections)
				{
					con.Dispose();
				}


				m_Host.Flush();
			}

			m_Host.Dispose(true);
			m_PacketsToSend.Dispose();
			m_PacketsChannelToUse.Dispose();
			m_ConnectionVersions.Dispose();
			m_QueuedForDisconnection.Dispose();
			m_Connections.Dispose();
			m_QueuedConnections.Dispose();
			m_PipelineReliableIds.Dispose();
			m_PipelineUnreliableIds.Dispose();
		}

		private bool m_DidBind;

		public bool IsCreated => Host.IsCreated;

		public JobHandle ScheduleUpdate(JobHandle dep = default(JobHandle))
		{
			if (!IsCreated)
				return dep;

			dep = new CleanJob {driver = this}.Schedule(dep);
			dep = new UpdateJob {driver = this}.Schedule(dep);
			return dep;
		}

		public int Bind(NetworkEndPoint endpoint)
		{
			throw new NotImplementedException("Please Use 'Bind(Valve.Sockets.Address address)'");
		}

		public int Bind(Address address)
		{
			m_Address = address;
			m_DidBind = m_Host.Create(address, (int) MaxConnections, 32);
			if (m_DidBind)
				return 0;
			return -1;
		}

		public int Listen()
		{
			if (!m_DidBind)
				throw new InvalidOperationException("Driver did not bind.");
			if (Listening)
				throw new InvalidOperationException("This driver is already listening.");

			Listening = true;
			return 0;
		}

		public bool Listening { get; private set; }

		public NetworkConnection Accept()
		{
			if (!Listening)
				return default;

			if (!m_QueuedConnections.TryDequeue(out var id))
				return default;

			return new NetworkConnection {m_NetworkId = (int) id, m_NetworkVersion = 1};
		}

		public NetworkConnection Connect(NetworkEndPoint endpoint)
		{
			throw new NotImplementedException("Please Use 'Connect(Valve.Sockets.Address address)'");
		}

		public NetworkConnection Connect(Address address)
		{
			if (m_DidBind)
				throw new InvalidOperationException("Cant connecting when bind");

			if (!Host.IsCreated)
			{
				var created = m_Host.Create(1, 32);
				if (!created)
					throw new NotImplementedException("Failed to create");
			}

			Debug.Log($"{address.GetIP()} {address.Port}");
			var peer = m_Host.Connect(address, 32);
			AddConnection(peer);

			return new NetworkConnection {m_NetworkId = (int) peer.ID, m_NetworkVersion = 1};
		}

		public int Disconnect(NetworkConnection con)
		{
			if (!con.IsCreated)
				return 0;

			if (m_Connections.TryGetValue((uint) con.m_NetworkId, out var connection))
			{
				Debug.LogError($"DisconnectLater " + con.m_NetworkId);
				connection.Peer.DisconnectLater(0);
				return 0;
			}

			return -1;
		}

		public NetworkConnection.State GetConnectionState(NetworkConnection con)
		{
			if (m_Connections.TryGetValue((uint) con.m_NetworkId, out var connection))
			{
				switch (connection.Peer.State)
				{
					case PeerState.Disconnecting:
					case PeerState.Disconnected:
						return NetworkConnection.State.Disconnected;

					case PeerState.ConnectionPending:
						return NetworkConnection.State.AwaitingResponse;

					case PeerState.Connecting:
						return NetworkConnection.State.Connecting;

					case PeerState.Connected:
						return NetworkConnection.State.Connected;
				}
			}

			return NetworkConnection.State.Disconnected;
		}

		public NetworkEndPoint RemoteEndPoint(NetworkConnection con)
		{
			return NetworkEndPoint.AnyIpv4;
		}

		public NetworkEndPoint LocalEndPoint()
		{
			return NetworkEndPoint.AnyIpv4;
		}

		public NetworkPipeline CreatePipeline(params Type[] stages)
		{
			var isReliable = false;
			foreach (var pipe in stages)
			{
				if (pipe == typeof(ReliableSequencedPipelineStage))
				{
					isReliable = true;
					break;
				}
			}

			if (isReliable) m_PipelineReliableIds.Add(m_PipelineCount);
			else m_PipelineUnreliableIds.Add(m_PipelineCount);

			m_PipelineCount++;

			return new NetworkPipeline {Id = m_PipelineCount - 1};
		}

		public int Send(NetworkPipeline pipe, NetworkConnection con, DataStreamWriter strm)
		{
			return Send(pipe, con, (IntPtr) strm.GetUnsafeReadOnlyPtr(), strm.Length);
		}

		public int Send(NetworkPipeline pipe, NetworkConnection con, IntPtr data, int len)
		{
			if (!m_Connections.TryGetValue((uint) con.InternalId, out var connection))
				return -2;

			var packet = new Packet();
			{
				if (m_PipelineReliableIds.Contains(pipe.GetHashCode()))
				{
					packet.Create(data, len, PacketFlags.Reliable);
					m_PacketsToSend.Add(packet);
					m_PacketsChannelToUse.Add(new SendPacket {Peer = connection.Peer, Channel = (byte) pipe.Id});
					return 0;
				}

				if (pipe == NetworkPipeline.Null || m_PipelineUnreliableIds.Contains(pipe.GetHashCode()))
				{
					packet.Create(data, len, PacketFlags.None);
					m_PacketsToSend.Add(packet);
					m_PacketsChannelToUse.Add(new SendPacket {Peer = connection.Peer, Channel = (byte) pipe.Id});
					return 0;
				}
			}

			return -1;
		}

		public NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader bs)
		{
			con = default;
			bs  = default;

			var values = m_Connections.GetValueArray(Allocator.Temp);
			for (var i = 0; i != values.Length; i++)
			{
				con = new NetworkConnection {m_NetworkId = (int) values[i].Id, m_NetworkVersion = 1};
				var ev = values[i].PopEvent(out bs);
				if (ev != NetworkEvent.Type.Empty)
					return ev;
			}

			return NetworkEvent.Type.Empty;
		}

		public NetworkEvent.Type PopEventForConnection(NetworkConnection con, out DataStreamReader bs)
		{
			bs = default;
			if (!m_Connections.TryGetValue((uint) con.m_NetworkId, out var connection))
			{
				//throw new InvalidOperationException($"No connection with id '{(uint) con.m_NetworkId}' found.");
				// instead just throw a disconnection event...
				return NetworkEvent.Type.Disconnect;
			}

			return connection.PopEvent(out bs);
		}

		private Connection AddConnection(Peer peer)
		{
			var con = Connection.Create(peer);
			m_Connections.TryAdd(peer.ID, con);
			m_QueuedForDisconnection[(int) peer.ID] = false;
			return con;
		}

		private void RemoveConnection(uint connectionId)
		{
			m_Connections[connectionId].Dispose();
			m_Connections.Remove(connectionId);
			m_QueuedForDisconnection[(int) connectionId] = false;
		}
	}
}
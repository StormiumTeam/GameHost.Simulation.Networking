using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Valve.Sockets
{
#if VALVE_DRIVER
	public unsafe struct ValveDriver : INetworkDriver
	{
		private struct Connection : IDisposable
		{
			public uint Id;

			private UnsafeList* m_IncomingEvents;
			private UnsafeList* m_IncomingMessages;

			public int IncomingEventCount   => m_IncomingEvents->Length;
			public int IncomingMessageCount => m_IncomingMessages->Length;

			public static Connection Create(uint id)
			{
				Connection connection;
				connection.Id = id;
				connection.m_IncomingMessages = UnsafeList.Create
				(
					UnsafeUtility.SizeOf<NetworkingMessage>(),
					UnsafeUtility.AlignOf<NetworkingMessage>(),
					8,
					Allocator.Persistent,
					NativeArrayOptions.ClearMemory
				);
				connection.m_IncomingEvents = UnsafeList.Create
				(
					UnsafeUtility.SizeOf<NetworkEvent.Type>(),
					UnsafeUtility.AlignOf<NetworkEvent.Type>(),
					8,
					Allocator.Persistent,
					NativeArrayOptions.ClearMemory
				);
				return connection;
			}

			public void AddEvent(NetworkEvent.Type type)
			{
				m_IncomingEvents->Add(type);
			}

			public void AddMessage(NetworkingMessage message)
			{
#if UNITY_ENABLE_COLLECTION_CHECKS
				if (m_IncomingMessages.Ptr == null)
					throw new NullReferenceException();
#endif

				Debug.Log($"add - messageptr = {message.release}");
				m_IncomingMessages->Add(message);
				AddEvent(NetworkEvent.Type.Data);
			}

			public NetworkEvent.Type PopEvent()
			{
				if (m_IncomingEvents->Length == 0)
					return NetworkEvent.Type.Empty;

				var ev = UnsafeUtility.ReadArrayElement<NetworkEvent.Type>(m_IncomingEvents->Ptr, 0);
				if (m_IncomingEvents->Length > 1)
					m_IncomingEvents->RemoveAtSwapBack<NetworkEvent.Type>(0);
				return ev;
			}

			public bool PopMessage(out NetworkingMessage message)
			{
				if (m_IncomingMessages->Length > 0)
				{
					message = UnsafeUtility.ReadArrayElement<NetworkingMessage>(m_IncomingMessages->Ptr, 0);
					if (m_IncomingMessages->Length > 1)
						m_IncomingMessages->RemoveAtSwapBack<NetworkingMessage>(0);
					Debug.Log($"pop - messageptr = {message.release}");

					return true;
				}

				message = default;
				return false;
			}

			public void Dispose()
			{
				m_IncomingEvents->Dispose();
				m_IncomingMessages->Dispose();
			}
		}

		private struct CleanJob : IJob
		{
			public ValveDriver driver;

			public void Execute()
			{
				driver.m_DataStream.Clear();
				var connections = driver.m_Connections.GetValueArray(Allocator.Temp);
				for (var i = 0; i != connections.Length; i++)
				{
					if (connections[i].IncomingEventCount > 0)
						throw new InvalidOperationException("A connection still had events in queue!");
					if (connections[i].IncomingMessageCount > 0)
						throw new InvalidOperationException("A connection still had messages in queue!");
				}
			}
		}

		private struct UpdateJob : IJob
		{
			public ValveDriver driver;

			public void Execute()
			{
				driver.m_Sockets.DispatchCallback(driver.m_UpdateStatusMethod, (IntPtr) UnsafeUtility.AddressOf(ref driver));

				NativeArray<NetworkingMessage> messages;
				if (driver.m_ListeningSocket != 0)
				{
					messages = driver.m_Sockets.ReceiveMessagesOnListenSocket(driver.m_ListeningSocket);
				}
				else
				{
					messages = driver.m_Sockets.ReceiveMessagesOnConnection(driver.m_Connection);
				}

				if (messages.IsCreated && messages.Length > 0)
				{
					for (var i = 0; i != messages.Length; i++)
					{
						var msg = messages[i];
						if (!driver.m_Connections.TryGetValue(msg.connection, out var connection))
						{
							connection = driver.AddConnection(msg.connection);
							// this connection is new, add a connect event first
							connection.AddEvent(NetworkEvent.Type.Connect);
						}

						connection.AddMessage(msg);
					}
				}
			}
		}

		// keep it static.
		private static void OnConnectionUpdateStatus(StatusInfo status, IntPtr ptr)
		{
			var driver = UnsafeUtilityEx.AsRef<ValveDriver>(ptr.ToPointer());
			Debug.Log(driver.Listening + " : " + status.connectionInfo.state);
			
			if (!driver.m_Connections.TryGetValue(status.connection, out var connection))
			{
				connection = driver.AddConnection(status.connection);
			}

			switch (status.connectionInfo.state)
			{
				case ConnectionState.None:
					break;
				case ConnectionState.Connecting:
					driver.Sockets.AcceptConnection(status.connection);
					//driver.m_QueuedConnections.Enqueue(status.connection);
					break;
				case ConnectionState.Connected:
					connection.AddEvent(NetworkEvent.Type.Connect);
					break;
				case ConnectionState.ProblemDetectedLocally:
				case ConnectionState.ClosedByPeer:
				{
					connection.AddEvent(NetworkEvent.Type.Disconnect);
					if (driver.m_Connections.ContainsKey(connection.Id))
					{
						driver.RemoveConnection(connection.Id);
					}

					break;
				}

				default:
					throw new ArgumentOutOfRangeException();
			}
		}

		/// <summary>
		/// The sockets that is currently used
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private NetworkingSockets m_Sockets;

		/// <summary>
		/// The bind address
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private Address m_Address;

		/// <summary>
		/// The current listening ID from the server
		/// </summary>
		private uint m_ListeningSocket;

		/// <summary>
		/// The current connection ID from the client to the server
		/// </summary>
		private uint m_Connection;

		/// <summary>
		/// The function used for connection status change
		/// </summary>
		[NativeDisableUnsafePtrRestriction]
		private IntPtr m_UpdateStatusMethod;

		private NativeHashMap<uint, Connection> m_Connections;
		private NativeQueue<uint>               m_QueuedConnections;
		private NativeList<int>                 m_PipelineReliableIds;
		private NativeList<int>                 m_PipelineUnreliableIds;
		private int                             m_PipelineCount;

		public NetworkingSockets Sockets => m_Sockets;

		private DataStreamWriter m_DataStream;

		public ValveDriver(int temp)
		{
			this = default;

			m_Sockets = NetworkingSockets.Create();
			m_Connections = new NativeHashMap<uint, Connection>(32, Allocator.Persistent);
			m_QueuedConnections = new NativeQueue<uint>(Allocator.Persistent);
			m_PipelineReliableIds = new NativeList<int>(Allocator.Persistent);
			m_PipelineUnreliableIds = new NativeList<int>(Allocator.Persistent);
			m_DataStream = new DataStreamWriter(4096, Allocator.Persistent);
			m_PipelineCount++;

			m_UpdateStatusMethod = Marshal.GetFunctionPointerForDelegate<StatusCallback>(OnConnectionUpdateStatus);
		}

		public void Dispose()
		{
			if (IsCreated)
			{
				var connections = m_Connections.GetValueArray(Allocator.Temp);
				foreach (var con in connections)
				{
					if (!Listening && Library.Initialized) 
						m_Sockets.CloseConnection(con.Id);
					con.Dispose();
				}

				if (Listening && Library.Initialized)
				{
					m_Sockets.CloseListenSocket(m_ListeningSocket);
				}
			}

			m_Connections.Dispose();
			m_QueuedConnections.Dispose();
			m_PipelineReliableIds.Dispose();
			m_PipelineUnreliableIds.Dispose();
			m_DataStream.Dispose();
		}

		public bool IsCreated => m_Sockets.IsCreated;

		public JobHandle ScheduleUpdate(JobHandle dep = default(JobHandle))
		{
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
			return 0;
		}

		public int Listen()
		{
			if (m_ListeningSocket != 0)
				throw new InvalidOperationException("This driver is already listening.");

			m_ListeningSocket = m_Sockets.CreateListenSocket(ref m_Address);
			if (m_ListeningSocket >= 0)
				return 0;
			return -1;
		}

		public bool Listening => m_ListeningSocket != 0;

		public NetworkConnection Accept()
		{
			if (Listening)
				return default;

			if (!m_QueuedConnections.TryDequeue(out var id))
				return default;
			m_Sockets.AcceptConnection(id);
			return new NetworkConnection {m_NetworkId = (int) id, m_NetworkVersion = 1};
		}

		public NetworkConnection Connect(NetworkEndPoint endpoint)
		{
			throw new NotImplementedException("Please Use 'Connect(Valve.Sockets.Address address)'");
		}

		public NetworkConnection Connect(Address address)
		{
			var connectionId = m_Sockets.Connect(ref address);
			var con = new NetworkConnection {m_NetworkId = (int) connectionId, m_NetworkVersion = 1};
			AddConnection(connectionId);
			
			return con;
		}

		public int Disconnect(NetworkConnection con)
		{
			if (!con.IsCreated)
				return 0;

			m_Sockets.CloseConnection((uint) con.m_NetworkId);
			return 0;
		}

		public NetworkConnection.State GetConnectionState(NetworkConnection con)
		{
			ConnectionStatus status = default;
			if (!m_Sockets.GetQuickConnectionStatus((uint) con.m_NetworkId, ref status))
				return NetworkConnection.State.Disconnected;
			switch (status.state)
			{
				case ConnectionState.Connecting:
					return NetworkConnection.State.Connecting;
				case ConnectionState.Connected:
					return NetworkConnection.State.Connected;
				case ConnectionState.ClosedByPeer:
				case ConnectionState.ProblemDetectedLocally:
					return NetworkConnection.State.Disconnected;
				case ConnectionState.FindingRoute:
					return NetworkConnection.State.AwaitingResponse;
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
			if (m_PipelineReliableIds.Contains(pipe.GetHashCode()))
			{
				m_Sockets.SendMessageToConnection((uint) con.m_NetworkId, data, len, SendType.Reliable);
				return 0;
			}

			if (pipe == NetworkPipeline.Null || m_PipelineUnreliableIds.Contains(pipe.GetHashCode()))
			{
				m_Sockets.SendMessageToConnection((uint) con.m_NetworkId, data, len, SendType.Unreliable);
				return 0;
			}

			return -1;
		}

		public NetworkEvent.Type PopEvent(out NetworkConnection con, out DataStreamReader bs)
		{
			con = default;
			bs = default;

			var values = m_Connections.GetValueArray(Allocator.Temp);
			for (var i = 0; i != values.Length; i++)
			{
				con = new NetworkConnection {m_NetworkId = (int) values[i].Id, m_NetworkVersion = 1};
				var ev = PopEventForConnection(con, out bs);
				if (ev != NetworkEvent.Type.Empty)
					return ev;
			}

			return NetworkEvent.Type.Empty;
		}

		public NetworkEvent.Type PopEventForConnection(NetworkConnection con, out DataStreamReader bs)
		{
			bs = default;
			if (!m_Connections.TryGetValue((uint) con.m_NetworkId, out var connection))
				throw new InvalidOperationException($"No connection with id '{(uint) con.m_NetworkId}' found.");

			var ev = connection.PopEvent();
			if (ev == NetworkEvent.Type.Data)
			{
				if (!connection.PopMessage(out var message))
					throw new InvalidOperationException("A message was expected");

				var prevLen = m_DataStream.Length;
				m_DataStream.WriteBytes((byte*) message.data, message.length);
				bs = new DataStreamReader(m_DataStream, prevLen, m_DataStream.Length);

				// native destroy
				message.Destroy();
			}

			return ev;
		}

		private Connection AddConnection(uint connectionId)
		{
			var con = Connection.Create(connectionId);
			m_Connections.TryAdd(connectionId, con);
			return con;
		}

		private void RemoveConnection(uint connectionId)
		{
			m_Connections[connectionId].Dispose();
			m_Connections.Remove(connectionId);
		}
	}
#endif
}
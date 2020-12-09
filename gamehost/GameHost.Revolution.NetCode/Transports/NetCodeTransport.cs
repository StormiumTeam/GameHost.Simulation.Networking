using System;
using System.Collections.Generic;
using System.Net;
using GameHost.Core.IO;

namespace GameHost.Revolution.NetCode.LLAPI
{
	// WIP
	/*public class NetCodeTransport : TransportDriver
	{
		private Server?                                           server;
		private Client?                                           client;
		private Dictionary<TransportConnection, ReliableEndpoint> reliableEndpoint;

		public NetCodeTransport()
		{
			reliableEndpoint = new Dictionary<TransportConnection, ReliableEndpoint>();
		}

		public void CreateServer(int maxSlots, IPEndPoint endPoint, ulong protocolId, byte[] privateKey)
		{
			if (client != null)
				throw new InvalidOperationException("a client is already created on this driver");

			server = new Server(maxSlots, endPoint.Address.ToString(), endPoint.Port, protocolId, privateKey);
		}

		public void CreateClient(byte[] connectToken)
		{
			if (server != null)
				throw new InvalidOperationException("a server is already created on this driver");

			client = new Client();
			client.Connect(connectToken);
		}

		public override TransportAddress          TransportAddress { get; }
		
		public override TransportConnection       Accept()
		{
			
		}

		public override void                      Update()
		{
			
		}

		public override TransportEvent            PopEvent()
		{
			
		}

		public override TransportConnection.State GetConnectionState(TransportConnection con)
		{
			
		}

		public override int                       Send(TransportChannel                  chan, TransportConnection con, Span<byte> data)
		{
			
		}

		public override int                       Broadcast(TransportChannel             chan, Span<byte>          data)
		{
			reliableEndpoint.SendMessage();
		}

		public override void                      Dispose()
		{
			
		}
	}*/
}
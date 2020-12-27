using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using Microsoft.Extensions.Logging;
using ZLogger;

namespace GameHost.Revolution.NetCode.Rpc
{
	public interface INetCodeRpcSerializer : IInstigatorSystem
	{
		bool CanSerialize(Type        type);
		void Serialize(in   BitBuffer bitBuffer, Type type, Span<byte> data);
		void Deserialize(in BitBuffer bitBuffer);
	}

	public class NetCodeRpcBroadcaster : AppObject
	{
		private PooledDictionary<uint, IInstigatorSystem> serializers;
		private ILogger                                  logger;

		public readonly ISnapshotInstigator Source;

		public NetCodeRpcBroadcaster(Context context, ISnapshotInstigator toCopy) : base(context)
		{
			Source = toCopy;
			
			serializers      = toCopy.Serializers;
			typeToSerializer = new PooledDictionary<Type, INetCodeRpcSerializer>();

			send = new BitBuffer();
			system    = new BitBuffer();
			receive   = new BitBuffer();

			AddDisposable(serializers);
			AddDisposable(typeToSerializer);
			
			DependencyResolver.Add(() => ref logger);
		}

		private PooledDictionary<Type, INetCodeRpcSerializer> typeToSerializer;
		private BitBuffer                                     send;
		private BitBuffer                                     system;
		private BitBuffer                                     receive;

		public enum EQueue
		{
			Guaranteed,
			BeforeNextSnapshot
		}

		public void Queue<T>(T data, EQueue queueType = EQueue.Guaranteed)
			where T : struct
		{
			if (!typeToSerializer.TryGetValue(typeof(T), out var rpcSerializer))
			{
				foreach (var (id, obj) in serializers)
				{
					if (obj is not INetCodeRpcSerializer serializer || !serializer.CanSerialize(typeof(T)))
						continue;

					rpcSerializer = serializer;
					break;
				}

				typeToSerializer[typeof(T)] = rpcSerializer ?? throw new InvalidOperationException($"No serializer found for {typeof(T)}");
			}

			send.AddUInt(rpcSerializer.System.Id);

			system.Clear();
			rpcSerializer.Instigator = Source;
			rpcSerializer.Serialize(system, typeof(T), MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref data, 1)));
			send.AddBitBuffer(system);
		}

		public void Clear()
		{
			send.Clear();
		}

		public int GetData(PooledList<byte> bytes)
		{
			var length = send.Length;
			send.ToSpan(bytes.AddSpan(length));
			return length;
		}

		public void Receive(Span<byte> data)
		{
			receive.readPosition = 0;
			receive.nextPosition   = 0;
			receive.AddSpan(data);
			while (!receive.IsFinished)
			{
				var systemId = receive.ReadUInt();

				system.Clear();
				receive.ReadToExistingBuffer(system);
				if (serializers.TryGetValue(systemId, out var obj)
				    && obj is INetCodeRpcSerializer serializer)
				{
					serializer.Instigator = Source; 
					serializer.Deserialize(system);
				}
				else
				{
					logger?.ZLogWarning("No RpcSystem found with ID {0} (Length={1}bit)", systemId, system.nextPosition);
				}
			}
		}
	}
}
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.Rpc
{
	public struct RpcBaseType : RpcCommand
	{
		public void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex)
		{
			throw new NotImplementedException();
		}

		public void Serialize(DataStreamWriter writer)
		{
			throw new NotImplementedException();
		}

		public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			throw new NotImplementedException();
		}
	}
	
	public static class RpcBase
	{
		public unsafe delegate void u_Serialize(void* s, int size, DataStreamWriter data);
		public unsafe delegate void u_Deserialize(void* s, int size, DataStreamReader data, ref DataStreamReader.Context ctx);

		public unsafe struct Header
		{
			public int                            Id;
			public FunctionPointer<u_Serialize>   SerializeFunction;
			public FunctionPointer<u_Deserialize> DeserializeFunction;

			public void Serialize<T>(ref T parent, DataStreamWriter data)
				where T : struct, IRpcBase<T>
			{
				var ptr = UnsafeUtility.AddressOf(ref parent);
				SerializeFunction.Invoke(ptr, 0, data);
			}

			public void Deserialize<T>(ref T parent, DataStreamReader data, ref DataStreamReader.Context ctx)
				where T : struct, IRpcBase<T>
			{
				var ptr = UnsafeUtility.AddressOf(ref parent);
				DeserializeFunction.Invoke(ptr, 0, data, ref ctx);
			}
		}

		private static int                     m_Counter            = 1;
		private static Dictionary<Type, int>   m_ManagedToUnmanaged = new Dictionary<Type, int>();
		private static Dictionary<int, Header> m_RpcTypes           = new Dictionary<int, Header>();

		public static unsafe Header GetHeader<T>() where T : struct, IRpcBase<T>
		{
			var type = typeof(T);
			var tmp  = default(T);

			if (!m_ManagedToUnmanaged.ContainsKey(type))
			{
				tmp.SetupHeader(out var serializeFunction, out var deserializeFunction);

				var i = m_Counter++;

				m_ManagedToUnmanaged[type] = i;
				m_RpcTypes[i] = new Header
				{
					Id = i,
					SerializeFunction = new FunctionPointer<u_Serialize>(Marshal.GetFunctionPointerForDelegate(new u_Serialize((s, size, data) =>
					{
						ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
						serializeFunction(ref output, data);
					}))),
					DeserializeFunction = new FunctionPointer<u_Deserialize>(Marshal.GetFunctionPointerForDelegate(new u_Deserialize((void* s, int size, DataStreamReader data, ref DataStreamReader.Context ctx) =>
					{
						ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
						deserializeFunction(ref output, data, ref ctx);
					})))
				};
			}

			return m_RpcTypes[m_ManagedToUnmanaged[type]];
		}
	}

	public delegate void rpc_Serialize<T>(ref T s, DataStreamWriter data) where T : struct;

	public delegate void rpc_Deserialize<T>(ref T s, DataStreamReader data, ref DataStreamReader.Context ctx) where T : struct;

	public interface IRpcBase<T> where T : struct
	{
		RpcBase.Header Header { get; set; }

		void SetupHeader(out rpc_Serialize<T> serialize, out rpc_Deserialize<T> deserialize);
	}
}
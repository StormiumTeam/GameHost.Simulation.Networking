using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
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
		public unsafe delegate void u_Deserialize(void* s, int size, void* data, ref DataStreamReader.Context ctx);

		public unsafe delegate void u_Execute(void* s, int size, Entity entity, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex);

		public unsafe struct Header
		{
			public int                            Id;
			public FunctionPointer<u_Serialize>   SerializeFunction;
			public FunctionPointer<u_Deserialize> DeserializeFunction;
			public FunctionPointer<u_Execute> ExecuteFunction;
			
			public int Size, Align;

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
				DeserializeFunction.Invoke(ptr, 0, &data, ref ctx);
			}
		}
	}

	public interface IRpcBase<T> where T : struct
	{
		RpcBase.Header Header { get; set; }

		void Serialize(DataStreamWriter data);
		void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx);
		void Execute(Entity connection, EntityCommandBuffer.Concurrent commandBuffer, int jobIndex);
	}
}
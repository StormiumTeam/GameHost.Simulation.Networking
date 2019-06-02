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

		private static int                     m_Counter            = 0;
		private static Dictionary<Type, int>   m_ManagedToUnmanaged = new Dictionary<Type, int>();
		private static Dictionary<int, Header> m_RpcTypes           = new Dictionary<int, Header>();

		private static List<GCHandle> m_PinnedDelegates = new List<GCHandle>();

		static RpcBase()
		{
			Application.quitting += () =>
			{
				m_PinnedDelegates.ForEach((handle) => handle.Free());
				m_PinnedDelegates.Clear();

				m_ManagedToUnmanaged.Clear();
				m_RpcTypes.Clear();

				m_ManagedToUnmanaged = null;
				m_RpcTypes           = null;
				m_PinnedDelegates    = null;
			};
		}

		public static unsafe Header GetHeader<T>() where T : struct, IRpcBase<T>
		{
			var type = typeof(T);
			var tmp  = default(T);

			if (!m_ManagedToUnmanaged.ContainsKey(type))
			{
				/*m_PinnedDelegates.Add(GCHandle.Alloc(serializeFunction, GCHandleType.Pinned));
				m_PinnedDelegates.Add(GCHandle.Alloc(deserializeFunction, GCHandleType.Pinned));
				m_PinnedDelegates.Add(GCHandle.Alloc(executeFunction, GCHandleType.Pinned));*/

				var i = m_Counter;
				m_Counter++;

				m_ManagedToUnmanaged[type] = i;
				m_RpcTypes[i] = new Header
				{
					Id = i,
					Size = UnsafeUtility.SizeOf<T>(),
					Align = UnsafeUtility.AlignOf<T>(),
					SerializeFunction = new FunctionPointer<u_Serialize>(Marshal.GetFunctionPointerForDelegate(new u_Serialize((s, size, data) =>
					{
						ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
						output.Serialize(data);
					}))),
					DeserializeFunction = new FunctionPointer<u_Deserialize>(Marshal.GetFunctionPointerForDelegate(new u_Deserialize((void* s, int size, void* data, ref DataStreamReader.Context ctx) =>
					{
						ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
						UnsafeUtility.CopyPtrToStructure(data, out DataStreamReader r);
						
						output.Deserialize(r, ref ctx);
					}))),
					ExecuteFunction = new FunctionPointer<u_Execute>(Marshal.GetFunctionPointerForDelegate(new u_Execute((s, size, connection, commandBuffer, jobIndex) =>
					{
						ref var output = ref UnsafeUtilityEx.AsRef<T>(s);
						output.Execute(connection, commandBuffer, jobIndex);
					})))
				};
			}

			return m_RpcTypes[m_ManagedToUnmanaged[type]];
		}

		public static NativeList<Header> GetAllHeaders(Allocator tempJob)
		{
			var list = new NativeList<Header>(tempJob);
			foreach (var header in m_RpcTypes.Values)
			{
				list.Add(header);
			}

			return list;
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
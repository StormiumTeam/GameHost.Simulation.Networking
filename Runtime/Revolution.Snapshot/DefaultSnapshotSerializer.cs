using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using UnityEngine;

namespace Revolution
{
	public class DefaultSnapshotSerializer : CustomSnapshotSerializer
	{
		public override void Serialize(SerializeClientData jobData, NativeList<SortDelegate<OnSerializeSnapshot>> delegateSerializers, DataStreamWriter writer, NativeList<byte> outgoing, bool debugRange)
		{
			new SerializeJob
			{
				ClientData   = jobData,
				Serializers  = delegateSerializers,
				StreamWriter = writer,
				OutgoingData = outgoing,

				DebugRange = debugRange
			}.Run();
		}

		public override void Deserialize(DeserializeClientData jobData, NativeList<SortDelegate<OnDeserializeSnapshot>> delegateDeserializers, NativeArray<byte> data, NativeArray<DataStreamReader.Context> readCtxArray, bool debugRange)
		{
			new DeserializeJob
			{
				ClientData    = jobData,
				Deserializers = delegateDeserializers,
				StreamData    = data,
				ReadContext   = readCtxArray,

				DebugRange = debugRange
			}.Run();
		}

		[BurstCompile]
		public unsafe struct SerializeJob : IJob
		{
			public bool DebugRange;

			public NativeList<SortDelegate<OnSerializeSnapshot>> Serializers;
			public SerializeClientData                           ClientData;

			public DataStreamWriter StreamWriter;
			public NativeList<byte> OutgoingData;

			public void Execute()
			{
				var parameters = new SerializeParameters
				{
					m_ClientData = new Blittable<SerializeClientData>(ref ClientData),
					m_Stream     = new Blittable<DataStreamWriter>(ref StreamWriter)
				};
				if (DebugRange)
				{
					for (int i = 0, serializerLength = Serializers.Length; i != serializerLength; i++)
					{
						var serializer = Serializers[i];
						var invoke     = serializer.Value.Invoke;

						if (DebugRange)
							StreamWriter.Write(StreamWriter.Length);

						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}
				else
				{
					for (int i = 0, serializerLength = Serializers.Length; i != serializerLength; i++)
					{
						var serializer = Serializers[i];
						var invoke     = serializer.Value.Invoke;

						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}

				StreamWriter.Write(0);

				OutgoingData.AddRange(StreamWriter.GetUnsafePtr(), StreamWriter.Length);
			}
		}

		[BurstCompile]
		public struct DeserializeJob : IJob
		{
			public NativeList<SortDelegate<OnDeserializeSnapshot>> Deserializers;
			public DeserializeClientData                           ClientData;

			public NativeArray<byte>                     StreamData;
			public NativeArray<DataStreamReader.Context> ReadContext;

			public bool DebugRange;

			[BurstDiscard]
			private void ThrowError(int currLength, int byteRead, int i, SortDelegate<OnDeserializeSnapshot> serializer)
			{
				Debug.LogError($"Invalid Length [{currLength} != {byteRead}] at index {i}, system {serializer.Name.ToString()}, previous system {Deserializers[math.max(i - 1, 0)].Name.ToString()}");
			}

			public void Execute()
			{
				var reader = new DataStreamReader(StreamData);
				var parameters = new DeserializeParameters
				{
					m_ClientData = new Blittable<DeserializeClientData>(ref ClientData),
					Stream       = reader,
					Ctx          = ReadContext[0]
				};

				if (DebugRange)
				{
					for (var i = 0; i < Deserializers.Length; i++)
					{
						var serializer = Deserializers[i];
						var invoke     = serializer.Value.Invoke;

						if (DebugRange)
						{
							var byteRead   = reader.GetBytesRead(ref parameters.Ctx);
							var currLength = reader.ReadInt(ref parameters.Ctx);
							if (currLength != byteRead)
							{
								ThrowError(currLength, byteRead, i, serializer);
								return;
							}
						}

						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}
				else
				{
					for (int i = 0, deserializeLength = Deserializers.Length; i < deserializeLength; i++)
					{
						var serializer = Deserializers[i];
						var invoke     = serializer.Value.Invoke;

						parameters.SystemId = serializer.SystemId;
						invoke(ref parameters);
					}
				}

				ReadContext[0] = parameters.Ctx;
			}
		}
	}
}
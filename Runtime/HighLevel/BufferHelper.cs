using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;

namespace package.stormiumteam.networking.runtime.highlevel
{
	public static unsafe class BufferHelper
	{
		public static DataBufferWriter CreateFromPattern(int patternId, Allocator allocator = Allocator.Temp, int length = 0)
		{
			var buffer = new DataBufferWriter(length, allocator);

			buffer.WriteUnmanaged(MessageType.MessagePattern);
			buffer.WriteInt(patternId);

			return buffer;
		}

		public static DataBufferReader ReadEventAndGetType(NetworkEvent ev, out MessageType messageType)
		{
			var buffer = new DataBufferReader(ev.Data, ev.DataLength);
			messageType = buffer.ReadValue<MessageType>();
			
			return buffer;
		}

		public static DataBufferReader ReadEventAndGetPattern(NetworkEvent ev, PatternBankExchange exchange, out int patternId)
		{
			var buffer = ReadEventAndGetType(ev, out var msgType);
			if (msgType != MessageType.MessagePattern)
			{
				patternId = 0;
				return default;
			}

			patternId = exchange.GetOriginId(buffer.ReadValue<int>());

			return buffer;
		}
	}
}
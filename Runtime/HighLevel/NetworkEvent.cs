using System;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public enum NetworkEventType
    {
        None = 0,
        DataReceived = 1,
        Connected = 2,
        Disconnected = 3
    }
    
    public unsafe struct NetworkEvent
    {
        public NetworkEventType Type;
        public NetworkConnection Invoker;

        public byte* Data;
        public byte TimeoutForeignDisconnection;
        public uint ConnectionData;

        public NetworkEvent(NetworkEventType type, NetworkConnection invoker)
        {
            Type = type;
            Invoker = invoker;

            Data = default(byte*);

            TimeoutForeignDisconnection = 0;
            ConnectionData = 0;
        }

        public void SetData(NativeArray<byte> data)
        {
            Data = (byte*) data.GetUnsafeReadOnlyPtr();
        }
    }
}
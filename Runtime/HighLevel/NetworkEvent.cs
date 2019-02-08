using System;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public enum NetworkEventType
    {
        None = 0,
        DataReceived = 1,
        Connecting = 2,
        Connected = 3,
        Disconnected = 4
    }
    
    public unsafe struct NetworkEvent
    {
        public NetworkEventType Type;
        public NetworkConnection Invoker;
        public NetworkCommands InvokerCmds;

        public byte* Data;
        public int DataLength;
        
        public byte TimeoutForeignDisconnection;
        public uint ConnectionData;

        public NetworkEvent(NetworkEventType type, NetworkConnection invoker, NetworkCommands invokerCmds)
        {
            Type = type;
            Invoker = invoker;

            Data = default(byte*);
            DataLength = 0;

            TimeoutForeignDisconnection = 0;
            ConnectionData = 0;

            InvokerCmds = invokerCmds;
        }

        public void SetData(byte* data, int length)
        {
            Data = data;
            DataLength = length;
        }
    }
}
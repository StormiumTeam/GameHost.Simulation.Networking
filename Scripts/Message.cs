using LiteNetLib.Utils;

namespace package.stormiumteam.networking
{
    public enum MessageType
    {
        Unknow = 0,
        Internal = 1,
        Pattern = 2,
    }
    
    public static class MessageTypeExtension
    {
        public static bool IsPattern(this MessageType t) =>
            t == MessageType.Pattern;
    }

    public enum InternalMessageType
    {
        SendNetManagerConfig = 30,
        AddPattern = 31,
        DeployChannel = 32,
        SetChannelOption = 33,
        AllBroadcastedDataSent = 34,
        AllBroadcastedDataReceived = 35,
        AddUser = 36,
        RemUser = 37,
        SetUser = 38
    }
    
    public struct MessageReader
    {
        public int IntType;
        public NetDataReader Data;

        public void ResetReadPosition()
        {
            // hacky way, but I don't want to edit source files
            // I reset the position of the cursor by using SetSource(Byte[]) as it set it back to 0 
            // with keeping the original data.
            Data.SetSource(Data.Data);
            Data.GetByte(); //< skip the "Type" field
        }
        
        public MessageType Type => (MessageType)IntType;
    }
}
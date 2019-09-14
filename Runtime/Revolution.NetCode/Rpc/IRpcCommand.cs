using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
    public interface IRpcCommand
    {
        void Execute(Entity            connection, World world);
        void WriteTo(DataStreamWriter  writer);
        void ReadFrom(DataStreamReader reader, ref DataStreamReader.Context ctx);
    }
}
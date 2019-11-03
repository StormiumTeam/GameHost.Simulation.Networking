using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
{
    public interface IRpcCommandRequestComponentData : IComponentData
    {
        void Serialize(DataStreamWriter   writer);
        void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx);
        
        Entity SourceConnection { get; set; }
    }

    public interface IRpcCommandRequestExecuteNow : IRpcCommandRequestComponentData
    {
        void Execute(EntityManager em);
    }
}
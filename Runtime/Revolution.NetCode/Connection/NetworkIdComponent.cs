using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace Revolution.NetCode
{
    public struct NetworkIdComponent : IComponentData
    {
        public int Value;
    }

    internal struct RpcSetNetworkId : IRpcCommand<RpcSetNetworkId>
    {
        public int nid;

        public void Execute(Entity connection, World world)
        {
            if (world.EntityManager.HasComponent<NetworkIdComponent>(connection))
            {
                Debug.Log("what?");
                return;
            }
            
            world.EntityManager.AddComponentData(connection, new NetworkIdComponent {Value = nid});
        }

        public void WriteTo(DataStreamWriter writer)
        {
            writer.Write(nid);
        }

        public void ReadFrom(DataStreamReader reader, ref DataStreamReader.Context ctx)
        {
            nid = reader.ReadInt(ref ctx);
        }
    }
}
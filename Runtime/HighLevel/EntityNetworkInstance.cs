using System.Collections.Generic;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public struct NetworkInstanceData : IComponentData
    {
        public int          Id;
        public int          ParentId;
        public InstanceType InstanceType;
        public Entity Parent;
        public NetworkCommands Commands;

        public uint GnsConnectionId;

        public NetworkInstanceData(int id, int parentId, Entity parent, InstanceType instanceType, NetworkCommands commands, uint gnsConnectionId = 0)
        {
            Id           = id;
            ParentId     = parentId;
            Parent = parent;
            InstanceType = instanceType;
            Commands = commands;

            GnsConnectionId = gnsConnectionId;
        }

        public bool IsLocal()
        {
            return (InstanceType & InstanceType.Local) != 0;
        }

        public bool HasParent()
        {
            return ParentId != 0;
        }
    }

    public struct NetworkInstanceHost : IComponentData
    {
        public NetworkHost Host;

        public NetworkInstanceHost(NetworkHost host)
        {
            Host = host;
        }
    }

    public struct ConnectedInstance : IBufferElementData
    {
        public Entity Entity;
        public NetworkConnection Connection;

        public ConnectedInstance(Entity entity, NetworkConnection connection)
        {
            Entity = entity;
            Connection = connection;
        }
    }

    public struct NetworkInstanceSharedData : ISharedComponentData
    {
        public NativeList<NetworkConnection>      Connections;
        public Dictionary<int, NetworkConnection> MappedConnections;

        public NetworkInstanceSharedData(NativeList<NetworkConnection> connections)
        {
            Debug.Assert(connections.IsCreated, "connections.IsCreated");

            Connections       = connections;
            MappedConnections = new Dictionary<int, NetworkConnection>(connections.Capacity);
        }
    }

    public struct NetworkInstanceToClient : IComponentData
    {
        public Entity Target;

        public NetworkInstanceToClient(Entity target)
        {
            Debug.Assert(target != default(Entity), "target != default(Entity)");
            
            Target = target;
        }
    }

    public struct ClientToNetworkInstance : IComponentData
    {
        public Entity Target;

        public ClientToNetworkInstance(Entity target)
        {
            Debug.Assert(target != default(Entity), "target != default(Entity)");
            
            Target = target;
        }
    }

    public struct ClientTag : IComponentData
    {
        
    }
}
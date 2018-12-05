using System;
using System.Collections.Generic;
using ENet;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public struct NetworkInstanceData : IComponentData
    {
        public int          Id;
        public InstanceType InstanceType;

        public NetworkInstanceData(int id, InstanceType instanceType)
        {
            Id           = id;
            InstanceType = instanceType;
        }

        public bool IsLocal()
        {
            return (InstanceType & InstanceType.Local) != 0;
        }
    }

    public struct NetworkInstanceHost : IComponentData
    {
        public NetworkHost Host;

        public NetworkInstanceHost(NetworkHost host)
        {
            Host = host;
        }

        public void Destroy()
        {
            Host.Dispose();
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
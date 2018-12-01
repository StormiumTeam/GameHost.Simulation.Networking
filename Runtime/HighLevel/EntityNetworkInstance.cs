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

        [NativeDisableUnsafePtrRestriction]
        public IntPtr Pointer;

        public NetworkInstanceData(int id, InstanceType instanceType, IntPtr pointer)
        {
            Id           = id;
            InstanceType = instanceType;
            Pointer      = pointer;
        }

        public Peer GetPeer()
        {
            return new Peer(Pointer);
        }

        public Host GetManagedHost()
        {
            return new Host {NativeData = Pointer};
        }

        public NativeNetHost GetNativeHost()
        {
            return new NativeNetHost(Pointer);
        }

        public bool IsHostType()
        {
            return (InstanceType & InstanceType.Local) != 0;
        }

        public bool IsSameAs(Peer peer)
        {
            return peer.NativeData == Pointer;
        }
        
        public bool IsSameAs(Host host)
        {
            return host.NativeData == Pointer;
        }
        
        public bool IsSameAs(NativeNetHost host)
        {
            return host.NativeHost == Pointer;
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
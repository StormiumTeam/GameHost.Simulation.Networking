using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using ENet;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    [Flags]
    public enum InstanceType
    {
        Unknow      = 0,
        Local       = 1 << 0,
        Client      = 1 << 1,
        Server      = 1 << 2,
        LocalClient = Local | Client,
        LocalServer = Local | Server
    }

    [UpdateInGroup(typeof(UpdateLoop.IntNetworkManager))]
    public unsafe class NetworkManager : ComponentSystem
    {
        public struct StartServerResult
        {
            public bool IsError;
            public int  ErrorCode;

            public int InstanceId;
            public Entity Entity;
        }
        
        public struct StartClientResult
        {
            public bool IsError;
            public int  ErrorCode;

            public int ClientInstanceId;
            public int ServerInstanceId;
            public Entity ClientInstanceEntity;
            public Entity ServerInstanceEntity;
        }

        public struct GetIncomingInstanceResult
        {
            public bool IsError;
            
            public int InstanceId;
        }

        private ReadOnlyCollection<ScriptBehaviourManager> m_WorldBehaviourManagers;
        private Dictionary<int, Entity>                    m_InstanceToEntity;
        
        private int m_InstanceValidQueryId;

        public ComponentType   DataType        { get; private set; }
        public ComponentType   SharedDataType  { get; private set; }
        public ComponentType   DataHostType    { get; private set; }
        public ComponentType   QueryBufferType { get; private set; }
        public EntityArchetype LocalEntityArchetype { get; private set; }
        public EntityArchetype ForeignEntityArchetype { get; private set; }

        protected override void OnCreateManager()
        {
            DataType        = ComponentType.Create<NetworkInstanceData>();
            SharedDataType  = ComponentType.Create<NetworkInstanceSharedData>();
            DataHostType    = ComponentType.Create<NetworkInstanceHost>();
            QueryBufferType = ComponentType.Create<QueryBuffer>();
            LocalEntityArchetype = EntityManager.CreateArchetype(DataType, SharedDataType, DataHostType, QueryBufferType);
            ForeignEntityArchetype = EntityManager.CreateArchetype(DataType, SharedDataType, QueryBufferType);

            m_WorldBehaviourManagers = (ReadOnlyCollection<ScriptBehaviourManager>) World.BehaviourManagers;
            m_InstanceToEntity       = new Dictionary<int, Entity>();

            m_InstanceValidQueryId = QueryTypeManager.Create("IntNetMgr_InstanceValid");
        }

        protected override void OnUpdate()
        {

        }

        protected override void OnDestroyManager()
        {
            foreach (var instance in m_InstanceToEntity)
            {
                if (instance.Value == default(Entity)) continue;
                
                Stop(instance.Value);
            }
            
            m_InstanceToEntity.Clear();
        }

        public StartServerResult StartServer(IPEndPoint localEndPoint, NetDriverConfiguration driverConfiguration)
        {
            var connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            var driver      = new NetDriver(driverConfiguration);
            var bindResult  = driver.Bind(localEndPoint);
            if (bindResult != NetDriverBindError.Success)
            {
                Debug.Log($"StartServer({localEndPoint}, ...) error -> {(int) bindResult}");

                return new StartServerResult
                {
                    IsError   = true,
                    ErrorCode = (int) bindResult
                };
            }
            
            driver.Host.LockDispose = true;

            driver.Listen();

            var connection = NetworkConnection.New();
            var instanceId = connection.Id;

            var entity = EntityManager.CreateEntity(LocalEntityArchetype);
            EntityManager.SetComponentData(entity, new NetworkInstanceData(instanceId, InstanceType.LocalServer));
            EntityManager.SetComponentData(entity, new NetworkInstanceHost(new NetworkHost(connection, driver.Host.NativeData)));
            EntityManager.SetSharedComponentData(entity, new NetworkInstanceSharedData(connections));
            m_InstanceToEntity[instanceId] = entity;

            InternalOnNetworkInstanceAdded(instanceId, entity);

            return new StartServerResult
            {
                InstanceId = instanceId,
                Entity = entity
            };
        }

        public StartClientResult StartClient(IPEndPoint peerEndPoint, IPEndPoint localEndPoint, NetDriverConfiguration configuration)
        {
            configuration.PeerLimit = 1;
            
            var connections = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            var serverConnections = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            var driver      = new NetDriver(configuration);
            var bindResult  = driver.Bind(localEndPoint);
            if (bindResult != NetDriverBindError.Success)
            {
                Debug.Log($"StartClient({localEndPoint}, ...) error -> {(int) bindResult}");

                return new StartClientResult
                {
                    IsError   = true,
                    ErrorCode = (int) bindResult
                };
            }

            driver.Host.LockDispose = true;

            // Connect to the server, and get the server peer
            var peer = driver.Connect(peerEndPoint);
            // Get the server peer connection data struct (used for internal stuff).
            var serverConnection = NetworkConnection.New();
            var serverInstanceId = serverConnection.Id;

            // Create a new client connection
            var clientConnection = NetworkConnection.New(serverConnection.Id);
            var clientInstanceId = clientConnection.Id;

            var clientEntity = EntityManager.CreateEntity(LocalEntityArchetype);
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceData(clientInstanceId, InstanceType.LocalClient));
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceHost(new NetworkHost(clientConnection, driver.Host.NativeData)));
            EntityManager.SetSharedComponentData(clientEntity, new NetworkInstanceSharedData(connections));
            
            // The server will only have THIS client connection
            serverConnections.Add(clientConnection);

            var serverEntity = EntityManager.CreateEntity(ForeignEntityArchetype);
            EntityManager.SetComponentData(serverEntity, new NetworkInstanceData(clientInstanceId, InstanceType.Server));
            EntityManager.SetSharedComponentData(serverEntity, new NetworkInstanceSharedData(serverConnections));

            var queryBuffer = EntityManager.GetBuffer<QueryBuffer>(serverEntity);
            queryBuffer.Add(new QueryBuffer(m_InstanceValidQueryId, QueryStatus.Waiting));

            ENetPeerConnection serverPeerConnection;
            if (!ENetPeerConnection.GetOrCreate(peer, out serverPeerConnection))
            {
                serverPeerConnection.Connection     = serverConnection;
                serverPeerConnection.InstanceEntity = serverEntity;
            }
            else
            {
                throw new InvalidOperationException();
            }

            m_InstanceToEntity[clientInstanceId] = clientEntity;
            m_InstanceToEntity[serverInstanceId] = serverEntity;

            InternalOnNetworkInstanceAdded(clientInstanceId, clientEntity);
            InternalOnNetworkInstanceAdded(serverInstanceId, serverEntity);

            return new StartClientResult
            {
                ClientInstanceId = clientInstanceId,
                ServerInstanceId = serverInstanceId,

                ClientInstanceEntity = clientEntity,
                ServerInstanceEntity = serverEntity
            };
        }

        public GetIncomingInstanceResult GetIncomingInstance(Entity localOrigin, NetworkConnection foreignConnection)
        {
            var result = new GetIncomingInstanceResult
            {
                IsError = true
            };

            #region Errors Wall

            if (localOrigin == default(Entity))
            {
                Debug.LogError("The origin of the foreign entity is null");
                return result;
            }

            if (foreignConnection.Id == 0)
            {
                Debug.LogError("Foreign connection is invalid");
                return result;
            }

            if (foreignConnection.ParentId == 0)
            {
                Debug.LogError("Foreign connection has no parent.");
                return result;
            }

            #endregion

            var localInstanceData = EntityManager.GetComponentData<NetworkInstanceData>(localOrigin);
            if (foreignConnection.ParentId != localInstanceData.Id)
            {
                Debug.LogError(
                    $"Foreign connection parent is not the same as the local origin ({foreignConnection.ParentId} != {localInstanceData.Id})");
                return result;
            }

            var foreignConList = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            foreignConList.Add(new NetworkConnection(localInstanceData.Id));

            var foreignEntity = EntityManager.CreateEntity(LocalEntityArchetype);
            EntityManager.SetComponentData(foreignEntity, new NetworkInstanceData(foreignConnection.Id, InstanceType.Client));
            EntityManager.SetSharedComponentData(foreignEntity, new NetworkInstanceSharedData(foreignConList));

            m_InstanceToEntity[foreignConnection.Id] = foreignEntity;

            result.IsError    = false;
            result.InstanceId = foreignConnection.Id;

            return result;
        }

        public void Stop(Entity instance)
        {
            var instanceData = EntityManager.GetComponentData<NetworkInstanceData>(instance);
            if (instanceData.IsLocal() && EntityManager.HasComponent(instance, DataHostType))
            {
                var sharedData  = EntityManager.GetSharedComponentData<NetworkInstanceSharedData>(instance);
                var instanceHos = EntityManager.GetComponentData<NetworkInstanceHost>(instance);
                var host        = instanceHos.Host;
                host.Flush();
                host.Dispose();

                sharedData.Connections.Dispose();
                sharedData.MappedConnections.Clear();
            }

            EntityManager.DestroyEntity(instance);
        }

        public Entity GetNetworkInstanceEntity(int instanceId)
        {
            return m_InstanceToEntity[instanceId];
        }

        internal void InternalOnNetworkInstanceAdded(int instanceId, Entity entity)
        {
            for (int i = 0; i != m_WorldBehaviourManagers.Count; i++)
            {
                var scriptBehaviourMgr     = m_WorldBehaviourManagers[i];
                var networkComponentSystem = scriptBehaviourMgr as INetworkComponentSystem;
                if (networkComponentSystem != null)
                {
                    networkComponentSystem.OnNetworkInstanceAdded(instanceId, entity);
                }
            }
        }
    }
}
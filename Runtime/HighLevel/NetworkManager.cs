using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using ENet;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.runtime.highlevel
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
            public Entity InstanceEntity;
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
            EntityManager.SetComponentData(entity, new NetworkInstanceData(connection.Id, 0, default(Entity), InstanceType.LocalServer));
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
            var serverCon = NetworkConnection.New();

            // Create a new client connection
            var clientCon = NetworkConnection.New(serverCon.Id);
            
            // The server will only have THIS client connection
            serverConnections.Add(clientCon);

            var serverEntity = EntityManager.CreateEntity(ForeignEntityArchetype);
            EntityManager.SetComponentData(serverEntity, new NetworkInstanceData(serverCon.Id, 0, default(Entity), InstanceType.Server));
            EntityManager.SetSharedComponentData(serverEntity, new NetworkInstanceSharedData(serverConnections));
            
            var clientEntity = EntityManager.CreateEntity(LocalEntityArchetype);
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceData(clientCon.Id, clientCon.ParentId, serverEntity, InstanceType.LocalClient));
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceHost(new NetworkHost(clientCon, driver.Host.NativeData)));
            EntityManager.SetSharedComponentData(clientEntity, new NetworkInstanceSharedData(connections));

            var queryBuffer = EntityManager.GetBuffer<QueryBuffer>(serverEntity);
            queryBuffer.Add(new QueryBuffer(m_InstanceValidQueryId, QueryStatus.Waiting));

            ENetPeerConnection serverPeerConnection;
            if (!ENetPeerConnection.GetOrCreate(peer, out serverPeerConnection))
            {
                serverPeerConnection.Connection     = serverCon;
                serverPeerConnection.InstanceEntity = serverEntity;
            }
            else
            {
                throw new InvalidOperationException();
            }

            m_InstanceToEntity[clientCon.Id] = clientEntity;
            m_InstanceToEntity[serverCon.Id] = serverEntity;
            
            Debug.Log($"StartClient() -> Result (Cid: {clientCon.Id}, Sid: {serverCon.Id})");

            InternalOnNetworkInstanceAdded(clientCon.Id, clientEntity);
            InternalOnNetworkInstanceAdded(serverCon.Id, serverEntity);

            return new StartClientResult
            {
                ClientInstanceId = clientCon.Id,
                ServerInstanceId = serverCon.Id,

                ClientInstanceEntity = clientEntity,
                ServerInstanceEntity = serverEntity
            };
        }

        public GetIncomingInstanceResult GetIncomingInstance(Entity origin, NetworkInstanceData originData, NetworkConnection incomingConnection)
        {
            if (incomingConnection.ParentId != originData.Id && !originData.HasParent())
            {
                Debug.LogError($"Invalid parent. {incomingConnection.ParentId} != {originData.Id}");
                return new GetIncomingInstanceResult
                {
                    IsError = true
                };
            }

            Entity foreignEntity;
            if (m_InstanceToEntity.ContainsKey(incomingConnection.Id))
            {
                Debug.Log("Adding server...");
                foreignEntity = GetNetworkInstanceEntity(incomingConnection.Id);
                var foreignData = EntityManager.GetComponentData<NetworkInstanceData>(foreignEntity);
                if (foreignData.InstanceType != InstanceType.Server) Debug.LogError("Invalid");
                
                var validatorMgr = new NativeValidatorManager(EntityManager.GetBuffer<QueryBuffer>(foreignEntity));
                if (validatorMgr.Has(m_InstanceValidQueryId))
                    validatorMgr.Set(m_InstanceValidQueryId, QueryStatus.Valid);

                return new GetIncomingInstanceResult
                {
                    IsError        = false,
                    InstanceId     = incomingConnection.Id,
                    InstanceEntity = foreignEntity
                };
            }

            var foreignConList = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            foreignEntity  = EntityManager.CreateEntity(ForeignEntityArchetype);
            EntityManager.SetComponentData(foreignEntity, new NetworkInstanceData(incomingConnection.Id, originData.Id, origin, InstanceType.Client));
            EntityManager.SetSharedComponentData(foreignEntity, new NetworkInstanceSharedData(foreignConList));
            
            foreignConList.Add(new NetworkConnection(originData.Id, originData.ParentId));

            m_InstanceToEntity[incomingConnection.Id] = foreignEntity;

            return new GetIncomingInstanceResult
            {
                IsError    = false,
                InstanceId = incomingConnection.Id,
                InstanceEntity = foreignEntity
            };
        }

        /* public GetIncomingInstanceResult GetIncomingInstance(Entity localOrigin, NetworkConnection otherConnection)
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
 
             if (otherConnection.Id == 0)
             {
                 Debug.LogError("Foreign connection is invalid");
                 return result;
             }
 
             if (otherConnection.ParentId == 0)
             {
                 Debug.LogError("Foreign connection has no parent.");
                 return result;
             }
 
             #endregion
 
             var localInstanceData = EntityManager.GetComponentData<NetworkInstanceData>(localOrigin);
             if (otherConnection.ParentId != localInstanceData.Id)
             {
                 Debug.LogError(
                     $"Foreign connection parent is not the same as the local origin ({otherConnection.ParentId} != {localInstanceData.Id})");
                 return result;
             }
 
             var otherConList = new NativeList<NetworkConnection>(1, Allocator.Persistent);
             otherConList.Add(new NetworkConnection(localInstanceData.Id));
 
             var otherEntity = EntityManager.CreateEntity(LocalEntityArchetype);
             EntityManager.SetComponentData(otherEntity, new NetworkInstanceData(otherConnection.Id, otherConnection.ParentId, localOrigin, InstanceType.Client));
             EntityManager.SetSharedComponentData(otherEntity, new NetworkInstanceSharedData(otherConList));
 
             m_InstanceToEntity[otherConnection.Id] = otherEntity;
 
             result.IsError    = false;
             result.InstanceId = otherConnection.Id;
 
             return result;
         }*/

        public void Stop(Entity instance)
        {
            var instanceData = EntityManager.GetComponentData<NetworkInstanceData>(instance);
            if (instanceData.IsLocal() && EntityManager.HasComponent(instance, DataHostType))
            {
                var instanceHos = EntityManager.GetComponentData<NetworkInstanceHost>(instance);
                var host        = instanceHos.Host;
                host.Flush();
                host.Dispose();
            }
            
            var sharedData = EntityManager.GetSharedComponentData<NetworkInstanceSharedData>(instance);
            sharedData.Connections.Dispose();
            sharedData.MappedConnections.Clear();
            
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
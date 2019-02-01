using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;
using Valve.Sockets;

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
        
        // Gns.Connection to NetworkConnection.Id
        internal Dictionary<uint, NativeConnection> UglyPendingServerConnections;

        public int InstanceValidQueryId { get; private set; }
        public ComponentType   DataType        { get; private set; }
        public ComponentType   SharedDataType  { get; private set; }
        public ComponentType   DataHostType    { get; private set; }
        public ComponentType   QueryBufferType { get; private set; }
        public ComponentType ConnectedBufferType { get; private set; }
        public EntityArchetype LocalEntityArchetype { get; private set; }
        public EntityArchetype ForeignEntityArchetype { get; private set; }

        private static void DebugOutputCallback(int type, string message)
        {
            Debug.Log($"[Steam Error] [{(ESteamNetworkingSocketsDebugOutputType) type}] {message}");
        }

        // We don't initialize any library in the instance methods, as there can be multiple worlds running a NetworkManager
        static NetworkManager()
        {
            NativeConnection.StaticCreate();
            
            var initializeMsg = new StringBuilder(Library.maxErrorMessageLength);
            if (!Library.Initialize(initializeMsg))
            {
                Debug.LogError($"Couldn't initialize GameNetworkingSockets: {initializeMsg}");
            }
            
            Library.SetDebugCallback((int) ESteamNetworkingSocketsDebugOutputType.Msg, DebugOutputCallback);

            Application.quitting += Library.Deinitialize;
        }
        
        protected override void OnCreateManager()
        {
            DataType               = ComponentType.Create<NetworkInstanceData>();
            SharedDataType         = ComponentType.Create<NetworkInstanceSharedData>();
            DataHostType           = ComponentType.Create<NetworkInstanceHost>();
            QueryBufferType        = ComponentType.Create<QueryBuffer>();
            ConnectedBufferType    = ComponentType.Create<ConnectedInstance>();
            LocalEntityArchetype   = EntityManager.CreateArchetype(DataType, SharedDataType, DataHostType, QueryBufferType, ConnectedBufferType);
            ForeignEntityArchetype = EntityManager.CreateArchetype(DataType, SharedDataType, QueryBufferType, ConnectedBufferType);

            m_WorldBehaviourManagers = (ReadOnlyCollection<ScriptBehaviourManager>) World.BehaviourManagers;
            m_InstanceToEntity       = new Dictionary<int, Entity>();
            UglyPendingServerConnections = new Dictionary<uint, NativeConnection>();

            InstanceValidQueryId = QueryTypeManager.Create("IntNetMgr_InstanceValid");
        }

        protected override void OnUpdate()
        {

        }

        protected override void OnDestroyManager()
        {
            StopAll();
            
            m_InstanceToEntity.Clear();
        }

        public StartServerResult StartServer(IPEndPoint localEndPoint)
        {
            var connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            var driver      = new NetDriver(IntPtr.Zero);
            var address = new Address();
            
            if (localEndPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                Debug.Log("Set IPV4");
                address.SetIPv4(localEndPoint.Address.ToString(), (ushort) localEndPoint.Port);
            }
            else if (localEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Debug.Log("Set IPV6");
                address.SetIPv6(localEndPoint.Address.ToString(), (ushort) localEndPoint.Port);
            }
            else
            {
                Debug.LogError($"addressFamily={localEndPoint.AddressFamily}");

                return new StartServerResult
                {
                    IsError   = true,
                    ErrorCode = -8
                };
            }
            
            var bindResult  = driver.Listen(address, out var socketId);
            if (bindResult != NetDriverBindError.Success)
            {
                Debug.Log($"StartServer({localEndPoint}, ...) error -> {(int) bindResult}");

                return new StartServerResult
                {
                    IsError   = true,
                    ErrorCode = (int) bindResult
                };
            }

            var connection = NetworkConnection.New();
            var instanceId = connection.Id;

            var entity = EntityManager.CreateEntity(LocalEntityArchetype);
            var cmds = NetworkCommands.CreateFromListenSocket(driver.Sockets.NativeData, socketId);
            EntityManager.SetComponentData(entity, new NetworkInstanceData(connection.Id, 0, default(Entity), InstanceType.LocalServer, cmds));
            EntityManager.SetComponentData(entity, new NetworkInstanceHost(new NetworkHost(connection, driver.Sockets.NativeData, socketId)));
            EntityManager.SetSharedComponentData(entity, new NetworkInstanceSharedData(connections));
            m_InstanceToEntity[instanceId] = entity;

            InternalOnNetworkInstanceAdded(instanceId, entity);

            return new StartServerResult
            {
                InstanceId = instanceId,
                Entity = entity
            };
        }

        public StartClientResult StartClient(IPEndPoint peerEndPoint)
        {
            var connections = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            var serverConnections = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            var driver      = new NetDriver(IntPtr.Zero);
            
            var peerAddress = new Address();
            if (peerEndPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                Debug.Log("Set IPV4");
                peerAddress.SetIPv4(peerEndPoint.Address.ToString(), (ushort) peerEndPoint.Port);
            }
            else if (peerEndPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                Debug.Log("Set IPV6");
                peerAddress.SetIPv6(peerEndPoint.Address.ToString(), (ushort) peerEndPoint.Port);
            }
            else
            {
                Debug.LogError($"addressFamily={peerEndPoint.AddressFamily}");

                return new StartClientResult
                {
                    IsError   = true,
                    ErrorCode = -8
                };
            }

            var bindResult  = driver.Connect(peerAddress, out var serverConnectionId);
            Debug.Log($"ServerId: {serverConnectionId}");
            var socketPtr = driver.Sockets.NativeData;
            if (bindResult != NetDriverBindError.Success)
            {
                Debug.Log($"StartClient({peerEndPoint}) error -> {(int) bindResult}");

                return new StartClientResult
                {
                    IsError   = true,
                    ErrorCode = (int) bindResult
                };
            }

            // Get the server peer connection data struct (used for internal stuff).
            NativeConnection serverNativeCon;
            if (NativeConnection.GetOrCreate(socketPtr, serverConnectionId, out serverNativeCon))
            {
                throw new InvalidOperationException();
            }
            var serverCon = serverNativeCon.Connection;
            
            // this is ugly but we need to do that.
            UglyPendingServerConnections[serverConnectionId] = serverNativeCon;

            // Create a new client connection
            var clientCon = NetworkConnection.New(serverCon.Id);
            
            // The server will only have THIS client connection
            serverConnections.Add(clientCon);

            var serverEntity = EntityManager.CreateEntity(ForeignEntityArchetype);
            var serverCmds = NetworkCommands.CreateFromConnection(socketPtr, serverConnectionId);
            EntityManager.SetComponentData(serverEntity, new NetworkInstanceData(serverCon.Id, 0, default(Entity), InstanceType.Server, serverCmds, serverConnectionId));
            EntityManager.SetSharedComponentData(serverEntity, new NetworkInstanceSharedData(serverConnections));
            
            var clientEntity = EntityManager.CreateEntity(LocalEntityArchetype);
            var clientCmds = NetworkCommands.CreateFromListenSocket(socketPtr, 0); // we need to find a way to get the socket id
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceData(clientCon.Id, clientCon.ParentId, serverEntity, InstanceType.LocalClient, clientCmds));
            EntityManager.SetComponentData(clientEntity, new NetworkInstanceHost(new NetworkHost(clientCon, driver.Sockets.NativeData, 0)));
            EntityManager.SetSharedComponentData(clientEntity, new NetworkInstanceSharedData(connections));

            var queryBuffer = EntityManager.GetBuffer<QueryBuffer>(serverEntity);
            queryBuffer.Add(new QueryBuffer(InstanceValidQueryId, QueryStatus.Waiting));
            
            // Add ConnectedInstance element to server entity.
            var serverConnectedBuffer = EntityManager.GetBuffer<ConnectedInstance>(serverEntity);
            serverConnectedBuffer.Add(new ConnectedInstance(clientEntity, clientCon));
            
            // Add ConnectedInstance element to client entity.
            var clientConnectedBuffer = EntityManager.GetBuffer<ConnectedInstance>(clientEntity);
            clientConnectedBuffer.Add(new ConnectedInstance(serverEntity, serverCon));

            m_InstanceToEntity[clientCon.Id] = clientEntity;
            m_InstanceToEntity[serverCon.Id] = serverEntity;
            
            Debug.Log($"StartClient() -> Result (Cid: {clientCon.Id}, Sid: {serverCon.Id})");

            InternalOnNetworkInstanceAdded(clientCon.Id, clientEntity);
            InternalOnNetworkInstanceAdded(serverCon.Id, serverEntity);
            
            //EntityManager.SetName(clientEntity, $"<NET> Local Client #{clientCon.Id} ({clientEntity}) -> Host: #{serverCon.Id} ({serverEntity})");
            //EntityManager.SetName(serverEntity, $"<NET> Host Server #{serverCon.Id} ({serverEntity}) <- Local: #{clientCon.Id} ({clientEntity})");

            return new StartClientResult
            {
                ClientInstanceId = clientCon.Id,
                ServerInstanceId = serverCon.Id,

                ClientInstanceEntity = clientEntity,
                ServerInstanceEntity = serverEntity
            };
        }

        public GetIncomingInstanceResult GetIncomingInstance(Entity origin, NetworkInstanceData originData, NetworkConnection incomingConnection, 
                                                             NetworkCommands foreignCmds)
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
                if (validatorMgr.Has(InstanceValidQueryId))
                    validatorMgr.Set(InstanceValidQueryId, QueryStatus.Valid);

                return new GetIncomingInstanceResult
                {
                    IsError        = false,
                    InstanceId     = incomingConnection.Id,
                    InstanceEntity = foreignEntity
                };
            }

            var foreignConList = new NativeList<NetworkConnection>(1, Allocator.Persistent);
            foreignEntity  = EntityManager.CreateEntity(ForeignEntityArchetype);
            EntityManager.SetComponentData(foreignEntity, new NetworkInstanceData(incomingConnection.Id, originData.Id, origin, InstanceType.Client, foreignCmds));
            EntityManager.SetSharedComponentData(foreignEntity, new NetworkInstanceSharedData(foreignConList));
            
            foreignConList.Add(new NetworkConnection(originData.Id, originData.ParentId));

            m_InstanceToEntity[incomingConnection.Id] = foreignEntity;
            
            // Add ConnectedInstance element to origin entity.
            var originConnectedBuffer = EntityManager.GetBuffer<ConnectedInstance>(origin);
            originConnectedBuffer.Add(new ConnectedInstance(foreignEntity, incomingConnection));

            // Add ConnectedInstance element to foreign entity.
            var foreignConnectedBuffer = EntityManager.GetBuffer<ConnectedInstance>(foreignEntity);
            foreignConnectedBuffer.Add(new ConnectedInstance(origin, incomingConnection));
            
            InternalOnNetworkInstanceAdded(incomingConnection.Id, foreignEntity);

            return new GetIncomingInstanceResult
            {
                IsError    = false,
                InstanceId = incomingConnection.Id,
                InstanceEntity = foreignEntity
            };
        }

        public void StopAll()
        {
            foreach (var o in m_InstanceToEntity.Values.ToArray())
                Stop(o, false);
        }

        public void Stop(Entity instance, bool deleteChildConnections = true)
        {
            void FreeConnection(long netConId)
            {
                if (NativeConnection.TryGet(netConId, out var peerConnection))
                {
                    Debug.Log("Freeing...");
                    NativeConnection.Free(peerConnection);
                }
                else
                {
                    Debug.LogError($"???? {netConId}");
                }
            }
            
            if (instance == default)
            {
                Debug.Log("No instance to destroy.");
                return;
            }
            
            var instanceData = EntityManager.GetComponentData<NetworkInstanceData>(instance);
            if (instanceData.IsLocal() && EntityManager.HasComponent(instance, DataHostType))
            {
                var instanceHost = EntityManager.GetComponentData<NetworkInstanceHost>(instance);
                var host        = instanceHost.Host;
                host.Stop();
            }
            
            Debug.Log($"Removing instance, Id={instanceData.Id}, Type={instanceData.InstanceType}");
            
            var sharedData = EntityManager.GetSharedComponentData<NetworkInstanceSharedData>(instance);
            sharedData.Connections.Dispose();
            sharedData.MappedConnections.Clear();

            FreeConnection(instanceData.Id);

            var connectionList = EntityManager.GetBuffer<ConnectedInstance>(instance);
            for (var i = 0; i != connectionList.Length; i++)
            {
                var connection = connectionList[i];
                if (!EntityManager.Exists(connection.Entity))
                    continue;
                
                var data = EntityManager.GetComponentData<NetworkInstanceData>(connectionList[i].Entity);

                data.Commands.SendDisconnectSignal(0);
            }

            // Destroy all connections that are linked to the target instance.
            if (deleteChildConnections && instanceData.IsLocal())
            {
                using (var ecb = new EntityCommandBuffer(Allocator.Temp))
                {
                    var foreignGroup = GetComponentGroup(DataType, SharedDataType, QueryBufferType, ConnectedBufferType);
                    var entityArray  = foreignGroup.GetEntityArray();
                    var dataArray    = foreignGroup.GetComponentDataArray<NetworkInstanceData>();
                    for (var i = 0; i != entityArray.Length; i++)
                    {
                        if (
                            // Destroy clients from server...
                            (dataArray[i].HasParent() && dataArray[i].Parent == instance)
                            ||
                            // Destroy server from client...
                            (instanceData.HasParent() && instanceData.Parent == entityArray[i])
                        )
                        {
                            FreeConnection(dataArray[i].Id);

                            dataArray[i].Commands.SendDisconnectSignal(0);
                            
                            ecb.DestroyEntity(entityArray[i]);
                            if (m_InstanceToEntity.ContainsKey(dataArray[i].Id))
                            {
                                m_InstanceToEntity[dataArray[i].Id] = default;
                            }
                            else
                            {
                                Debug.LogWarning($"Problem with {dataArray[i].Id} ({dataArray[i].InstanceType})");
                            }
                        }
                    }

                    ecb.Playback(EntityManager);
                }
            }

            if (m_InstanceToEntity.ContainsKey(instanceData.Id))
            {
                m_InstanceToEntity[instanceData.Id] = default;
            }
            else
            {
                Debug.LogWarning($"Problem with {instanceData.Id} ({instanceData.InstanceType})");
            }

            EntityManager.DestroyEntity(instance);
        }

        public Entity GetNetworkInstanceEntity(int instanceId)
        {            
            return m_InstanceToEntity[instanceId];
        }

        internal void InternalOnNetworkInstanceAdded(int instanceId, Entity entity)
        {
            if (NetConnectionEntityLink.TrySetEntity(instanceId, entity) < 0)
            {
                Debug.LogError($"Couldn't link entity {entity} for connection ({instanceId}).");
            }
            
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

        public uint GetGnsId(int dataParentId)
        {
            return 0;
        }
    }
}
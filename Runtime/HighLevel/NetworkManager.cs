using System;
using System.Collections.ObjectModel;
using System.Net;
using ENet;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    [Flags]
    public enum InstanceType
    {
        Unknow = 0,
        Local  = 1,
        Client = 2,
        Server = 4,
        /// <summary>
        /// Local + Client
        /// </summary>
        LocalClient = Local | Client,
        /// <summary>
        /// Local + Server
        /// </summary>
        LocalServer = Local | Server
    }
    
    [UpdateInGroup(typeof(UpdateLoop.IntNetworkManager))]
    public class NetworkManager : ComponentSystem
    {
        public struct StartServerResult
        {
            public bool IsError;
            public int ErrorCode;
            
            public int InstanceId;
        }

        private int m_InstanceCounter;
        private ReadOnlyCollection<ScriptBehaviourManager> m_WorldBehaviourManagers; 

        public ComponentType DataType { get; private set; }
        public ComponentType SharedDataType { get; private set; }
        public ComponentType QueryBufferType { get; private set; }
        public EntityArchetype EntityArchetype { get; private set; }

        protected override void OnCreateManager()
        {
            DataType = ComponentType.Create<NetworkInstanceData>();
            SharedDataType = ComponentType.Create<NetworkInstanceSharedData>();
            QueryBufferType = ComponentType.Create<QueryBuffer>();
            EntityArchetype = EntityManager.CreateArchetype(DataType, SharedDataType, QueryBufferType);

            m_InstanceCounter = 1;
            m_WorldBehaviourManagers = (ReadOnlyCollection<ScriptBehaviourManager>) World.BehaviourManagers;
        }

        protected override void OnUpdate()
        {
            
        }

        public StartServerResult StartServer(IPEndPoint localEndPoint, NetDriverConfiguration driverConfiguration)
        {
            var connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
            var sharedData = new NetworkInstanceSharedData(connections);
            var driver = new NetDriver(driverConfiguration);
            var bindResult = driver.Bind(localEndPoint);
            if (bindResult != NetDriverBindError.Success)
            {
                Debug.Log($"StartServer({localEndPoint}, ...) error -> {(int) bindResult}");
                
                return new StartServerResult
                {
                    IsError   = true,
                    ErrorCode = (int) bindResult
                };
            }
            
            driver.Listen();

            var instanceId = m_InstanceCounter++;
            var entity = EntityManager.CreateEntity(EntityArchetype);
            EntityManager.SetComponentData(entity, new NetworkInstanceData(instanceId, InstanceType.LocalServer, driver.Host.NativeData));
            EntityManager.SetSharedComponentData(entity, new NetworkInstanceSharedData(connections));

            for (int i = 0; i != m_WorldBehaviourManagers.Count; i++)
            {
                var scriptBehaviourMgr = m_WorldBehaviourManagers[i];
                var networkComponentSystem = scriptBehaviourMgr as INetworkComponentSystem;
                if (networkComponentSystem != null)
                {
                    networkComponentSystem.OnNetworkInstanceAdded(entity);
                }
            }

            return new StartServerResult
            {
                InstanceId = instanceId
            };
        }

        public void StartClient(IPEndPoint peerEndPoint, IPEndPoint localEndPoint)
        {
            
        }

        public NetworkInstanceData GetNetworkInstanceData(int instanceId)
        {
            Debug.Assert(instanceId > 0, "instanceId > 0");
            
            return new NetworkInstanceData
            {
                Id = instanceId
            };
        }
    }
}
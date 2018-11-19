using System;
using System.Runtime.InteropServices;
using package.stormiumteam.networking;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.ecs
{
    public struct EntityIsOnNetwork : IComponentData
    {
        
    }
    
    public struct NetworkEntity : IComponentData
    {
        public int InstanceId;
        public int NetId;
        public int NetVersion;
        
        // local variant
        public NetworkEntity(Entity entity)
        {
            InstanceId = -1;
            NetId      = entity.Index;
            NetVersion = entity.Version;
        }
        
        public NetworkEntity(int instanceId, Entity entity)
        {
            InstanceId = instanceId;
            NetId      = entity.Index;
            NetVersion = entity.Version;
        }

        public NetworkEntity(int instanceId, int netId, int netVersion)
        {
            InstanceId = instanceId;
            NetId = netId;
            NetVersion = netVersion;
        }

        public NetworkInstance GetNetworkInstance()
        {
            return NetworkInstance.FromId(InstanceId);
        }

        public Entity ToEntity()
        {
            return new Entity() {Index = NetId, Version = NetVersion};
        }
    }

    [RequireComponent(typeof(ReferencableGameObject), typeof(GameObjectEntity))]
    public class NetworkEntityComponent : MonoBehaviour
    {
        private ReferencableGameObject m_Referencable;
        private GameObjectEntity m_GameObjectEntity;

        public string OwnerAddress;
        public int NetId;
        public int NetVersion;
        
        private void Awake()
        {
            m_GameObjectEntity = ReferencableGameObject.GetComponent<GameObjectEntity>(gameObject);
            m_Referencable = GetComponent<ReferencableGameObject>();
        }

        #if UNITY_EDITOR
        private void Update()
        {
            var e = m_GameObjectEntity.Entity;
            var em = m_GameObjectEntity.EntityManager;
            if (em.HasComponent<NetworkEntity>(e))
            {
                var networkEntity = em.GetComponentData<NetworkEntity>(e);

                OwnerAddress = (networkEntity.GetNetworkInstance()?.World.Name ?? "Not valid");
                NetId = networkEntity.NetId;
                NetVersion = networkEntity.NetVersion;
            }
        }
        #endif
    }
}
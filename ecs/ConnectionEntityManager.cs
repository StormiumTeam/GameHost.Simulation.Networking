using System;
using System.Collections.Generic;
using System.Reflection;
using package.stormiumteam.networking.plugins;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Scripts.Utilities;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking.ecs
{
    public enum OperationEntity
    {
        None = 0,
        Push = 1,
        Delete = 2
    }

    public enum EntityType
    {
        PureEntity,
        HybridEntity
    }

    public enum OperationComponent
    {
        None = 0,
        Push = 1,
        CreateOnce = 2,
        Delete = 4
    }

    public enum ComponentShareOption
    {
        Automatic = 0,
        Manual    = 1, 
    }

    public partial class ConnectionEntityManager : NetworkConnectionSystem,
                                                   EventReceiveData.IEv,
                                                   EventPeerConnected.IEv
    {        
        private EntityManager       m_MainEntityManager;
        private MsgIdRegisterSystem m_MainPatternManager;

        [Inject] private ConnectionTypeManager    m_TypeManager;
        [Inject] private ConnectionPatternManager m_ConnectionPatternManager;
        [Inject] private ConnectionMessageSystem  m_MessageSystem;
        [Inject] private ConnectionUserManager    m_ConnectionUserManager;

        private Dictionary<long, Entity> m_MappedEntities = new Dictionary<long, Entity>();
        private Queue<Entity> m_SendNewBuffer = new Queue<Entity>();
        private Queue<NetworkEntity> m_SendNewEntity = new Queue<NetworkEntity>();
        private NativeList<byte> m_SerializedData = new NativeList<byte>(256, Allocator.Persistent);

        public MessageIdent MsgOperationEntity = new MessageIdent()
        {
            Id      = "networkentityV2.operation.entity",
            Version = 0
        };

        public MessageIdent MsgOperationComponent = new MessageIdent()
        {
            Id      = "networkentityV2.operation.component",
            Version = 0
        };

        protected override void OnCreateManager()
        {
            m_MainEntityManager  = MainWorld.GetOrCreateManager<EntityManager>();
            m_MainPatternManager = MainWorld.GetOrCreateManager<MsgIdRegisterSystem>();

            m_MainPatternManager.Register(MsgOperationEntity);
            m_MainPatternManager.Register(MsgOperationComponent);

            MainWorld.GetExistingManager<AppEventSystem>().SubscribeToAll(this);
        }

        protected override void OnUpdate()
        {
            ForceSync();
        }

        public void ForceSync()
        {
            // We are not connected
            if (!NetInstance.IsConnected)
                return;
                
            CheckTypes();
            
            foreach (var netEntity in m_SendNewEntity)
            {
                if (netEntity.InstanceId != NetInstance.Id)
                {
                    Debug.LogError("wrong instance");
                    return;
                }
                
                var entity = netEntity.ToEntity();
                if (!MainEntityMgr.Exists(entity))
                {
                    Debug.LogError
                    (
                        $"({entity}) was buffered for a network sent, but was removed. If you want to send an event like entity, use ForceSync() after invoking broadcast method"
                    );
                    
                    foreach (var inNetInstance in NetInstance.Interconnections)
                    {
                        var conEntityStock = inNetInstance.Get<ConnectionEntityDifferenceStock>();
                        conEntityStock.SetParent(this);
                        conEntityStock.RemEntity(entity);
                    }
                    
                    continue;
                }
                
                foreach (var inNetInstance in NetInstance.Interconnections)
                {
                    var conEntityStock = inNetInstance.Get<ConnectionEntityDifferenceStock>();
                    conEntityStock.SetParent(this);
                    conEntityStock.AddEntity(entity);
                }

                PrivateSendEntity(entity);
            }
            
            foreach (var entity in m_SendNewBuffer)
            {
                if (!MainEntityMgr.Exists(entity))
                {
                    Debug.LogError
                    (
                        $"({entity}) was buffered for a network sent, but was removed. If you want to send an event like entity, use ForceSync() after invoking broadcast method"
                    );
                    continue;
                }
                
                var buffer = MainEntityMgr.GetBuffer<NetworkEntityComponentSettingsBuffer>(entity);

                foreach (var inNetInstance in NetInstance.Interconnections)
                {
                    var conEntityStock = inNetInstance.Get<ConnectionEntityDifferenceStock>();
                    conEntityStock.SetParent(this);
                    conEntityStock.AddEntity(entity); // clear
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        conEntityStock.AddComponent(entity, buffer[i].ServerTypeIndex, buffer[i].ShareOption, buffer[i].DeliveryMethod, 0);
                    }
                }

                PrivateSendBuffer(entity, buffer);
            }
            
            m_SendNewEntity.Clear();
            m_SendNewBuffer.Clear();  
            
            if (m_SerializedData.Length > 256)
                m_SerializedData.ResizeUninitialized(256);
        }

        public bool HasEntity(NetworkEntity netEntity)
        {
            if (NetInstance.SelfHost)
            {
#if UNITY_EDITOR
                Debug.Assert(m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.NetId, netEntity.NetVersion)),
                    "m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.Index, netEntity.Version))");

                Debug.Assert(netEntity.InstanceId == NetInstance.Id, "netEntity.InstanceId == NetInstance.Id");
#endif

                return EntityManager.Exists(netEntity.ToEntity());
            }

            return m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.NetId, netEntity.NetVersion));
        }

        public bool HasEntity(Entity netEntity)
        {
            var key = GetDictionaryKey(netEntity);
            
            if (NetInstance.SelfHost)
            {
#if UNITY_EDITOR
                Debug.Assert(m_MappedEntities.ContainsKey(key),
                    "m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.Index, netEntity.Version))");
#endif

                return EntityManager.Exists(netEntity);
            }

            return m_MappedEntities.ContainsKey(key);
        }

        public void DestroyEntity(Entity netEntity)
        {
            var key = GetDictionaryKey(netEntity);
            
            if (NetInstance.SelfHost)
            {
                EntityManager.DestroyEntity(netEntity);
            }

            m_MappedEntities.Remove(key);
        }

        public Entity GetEntity(NetworkEntity netEntity)
        {
#if UNITY_EDITOR
            Debug.Assert(netEntity.InstanceId == NetInstance.Id, "netEntity.InstanceId == NetInstance.Id");
#endif

            return m_MappedEntities[GetDictionaryKey(netEntity.ToEntity())];
        }

        public Entity GetEntity(Entity netEntity)
        {
            if (NetInstance.SelfHost)
            {
                return netEntity;
            }

            return m_MappedEntities[GetDictionaryKey(netEntity)];
        }

        public void BroadcastEntity(NetworkEntity netEntity, EntityType entityType, bool instantSync = false)
        {
            if (instantSync)
            {
                var e = netEntity.ToEntity();
                
                PrivateSendEntity(e);
                if (MainEntityMgr.HasComponent(e, typeof(NetworkEntityComponentSettingsBuffer)))
                    PrivateSendBuffer(e, MainEntityMgr.GetBuffer<NetworkEntityComponentSettingsBuffer>(e));
                return;
            }
            
            if (!m_SendNewEntity.Contains(netEntity))
                m_SendNewEntity.Enqueue(netEntity);
            
            if (!m_SendNewBuffer.Contains(netEntity.ToEntity()))
                m_SendNewBuffer.Enqueue(netEntity.ToEntity());
        }

        public void SetShareOption(Entity entity, ComponentType type, ComponentShareOption option, DeliveryMethod deliver, bool instantSync = false)
        {
            if (!MainEntityMgr.HasComponent(entity, typeof(NetworkEntityComponentSettingsBuffer)))
            {
                MainEntityMgr.AddBuffer<NetworkEntityComponentSettingsBuffer>(entity);
            }

            var typeIndex = type.TypeIndex;
            var buffer    = MainEntityMgr.GetBuffer<NetworkEntityComponentSettingsBuffer>(entity);
            var key       = -1;
            for (int i = 0; key == -1 && i != buffer.Length; i++)
            {
                if (buffer[i].ServerTypeIndex != typeIndex) continue;

                key = i;
            }

            var value = new NetworkEntityComponentSettingsBuffer(typeIndex, option, deliver);
            if (key == -1)
                buffer.Add(value);
            else
                buffer[key] = value;

            if (instantSync)
            {
                PrivateSendBuffer(entity, buffer);
                return;
            }

            if (!m_SendNewBuffer.Contains(entity))
                m_SendNewBuffer.Enqueue(entity);
        }

        public void RemoveShare(Entity entity, ComponentType type)
        {
            // The entity don't have any buffer to begin with, so return
            if (!MainEntityMgr.HasComponent(entity, typeof(NetworkEntityComponentSettingsBuffer)))
            {
                return;
            }

            var typeIndex = type.TypeIndex;
            var buffer    = MainEntityMgr.GetBuffer<NetworkEntityComponentSettingsBuffer>(entity);
            var key       = -1;

            // The buffer is empty, so return
            if (buffer.Length == 0)
                return;

            for (int i = 0; key == -1 && i != buffer.Length; i++)
            {
                if (buffer[i].ServerTypeIndex != typeIndex) continue;

                key = i;
            }

            // Nothing found, so return
            if (key == -1)
                return;

            buffer.RemoveAt(key);

            if (!m_SendNewBuffer.Contains(entity))
                m_SendNewBuffer.Enqueue(entity);
        }

        public NetworkEntity ConvertAsNetworkable(EntityCommandBuffer ecb, Entity clientEntity, Entity serverEntity, NetworkInstance host = null)
        {
            host = host ?? NetInstance;

            var value = new NetworkEntity(host.Id, serverEntity);
            if (!MainEntityMgr.HasComponent(clientEntity, typeof(NetworkEntity)))
                ecb.AddComponent(clientEntity, value);
            else
                ecb.SetComponent(clientEntity, value);

            var key = GetDictionaryKey(serverEntity);

            if (host.SelfHost && HasEntity(serverEntity) && MainEntityMgr.Exists(clientEntity))
            {
                // That's a problem, but not an error...
                Debug.LogWarning($"An entity with the same id and version already exist! c: {clientEntity}, s: {serverEntity}");
                return value;
            }

            m_MappedEntities[key] = clientEntity;

            return value;
        }

        private void PrivateSendEntity(Entity entity)
        {
            var msg    = m_MessageSystem.Create(MsgOperationEntity);
            msg.Put((byte) OperationEntity.Push);
            msg.Put(entity);
            
            InternalSend(msg);
        }

        private void PrivateSendBuffer(Entity entity, DynamicBuffer<NetworkEntityComponentSettingsBuffer> buffer)
        {
            m_SerializedData.Clear();

            var entityMgr = MainEntityMgr;
            var typeIndex = TypeManager.GetTypeIndex(typeof(NetworkEntityComponentSettingsBuffer));
            var start     = m_SerializedData.Length;

            EntitySerializer.SerializeComponent(m_SerializedData, typeIndex, entity, entityMgr, true);

            var msg = GetPushComponentHeader(OperationComponent.Push, entity);
            msg.Put(m_SerializedData.Length);
            for (int i = start; i < m_SerializedData.Length; i++)
            {
                msg.Put(m_SerializedData[i]);
            }

            InternalSend(msg);
        }

        public NetDataWriter SerializeComponent(OperationComponent operation, Entity entity, int component)
        {
            m_SerializedData.Clear();
            
            EntitySerializer.SerializeComponent(m_SerializedData, component, entity, MainEntityMgr, true);
            
            var msg = GetPushComponentHeader(OperationComponent.Push, entity);
            msg.Put(m_SerializedData.Length);
            for (int i = 0; i < m_SerializedData.Length; i++)
            {
                msg.Put(m_SerializedData[i]);
            }

            return msg;
        }

        public NetDataWriter GetPushComponentHeader(OperationComponent operation, Entity entity)
        {
            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) OperationComponent.Push);
            msg.Put(entity);
            return msg;
        }

        private void InternalSend(NetDataWriter msg)
        {
            MainWorld.GetExistingManager<NetworkMessageSystem>().InstantSendToAll
            (
                NetInstance.GetDefaultChannel(),
                msg,
                DeliveryMethod.ReliableOrdered
            );
        }

        private void CheckTypes()
        {
            m_TypeManager.Update();
        }

        private void CheckTypes(Type anotherType)
        {
            TypeManager.GetTypeIndex(anotherType);
            CheckTypes();
        }

        private long GetDictionaryKey(Entity entity)
        {
            const bool useDoubleKey = true;
            
            var args1 = entity.Index;
            var args2 = entity.Version;

            return useDoubleKey ? StMath.DoubleIntToLong(args1, args2) : args1;
        }
    }

    public partial class ConnectionEntityManager
    {
        public void Callback(EventPeerConnected.Arguments args)
        {
            var peerInstance = args.PeerInstance;
            var conEntityStock = peerInstance.Get<ConnectionEntityDifferenceStock>();
        }
        
        public void Callback(EventReceiveData.Arguments args)
        {
            m_SerializedData.Clear();
            
            var reader       = args.Reader;
            var peerInstance = args.PeerInstance;

            if (reader.Type != MessageType.Pattern)
                return;
            
            var peerUserMgr    = peerInstance.GetUserManager();
            var peerPatternMgr = peerInstance.GetPatternManager();
            var peerEntityMgr  = peerInstance.Get<ConnectionEntityManager>();
            var peerTypeMgr    = peerInstance.Get<ConnectionTypeManager>();

            var msgPattern = peerPatternMgr.GetPattern(reader);
            if (msgPattern == MsgOperationEntity)
            {
                var operation = (OperationEntity)reader.Data.GetByte();
                var serverEntity = reader.Data.GetEntity();
                if (operation == OperationEntity.Push)
                {
                    if (peerEntityMgr.HasEntity(serverEntity))
                    {
                        var netEntityComponent = MainEntityMgr.GetComponentData<NetworkEntity>(peerEntityMgr.GetEntity(serverEntity));
                        if (netEntityComponent.InstanceId != args.PeerInstance.Global.Id)
                        {
                            peerEntityMgr.DestroyEntity(serverEntity);
                        }
                    }
                    else
                    {
                        var clientEntity = MainEntityMgr.CreateEntity();

                        using (var ecb = new EntityCommandBuffer(Allocator.Temp))
                        {
                            peerEntityMgr.ConvertAsNetworkable(ecb, clientEntity, serverEntity, args.PeerInstance.Global);
                            ecb.Playback(MainEntityMgr);
                        }
                    }
                }
            }
            else if (msgPattern == MsgOperationComponent)
            {
                var operation = (OperationComponent) reader.Data.GetByte();
                var entity = peerEntityMgr.GetEntity(reader.Data.GetEntity());
                var dataLength = reader.Data.GetInt();
                for (int i = 0; i != dataLength; i++)
                {
                    m_SerializedData.Add(reader.Data.GetByte());
                }

                var typeIndex = TypeManager.GetTypeIndex(peerTypeMgr.GetType(EntitySerializer.ReadInt(m_SerializedData, 0)));
                EntitySerializer.DeserializeComponent(m_SerializedData, 4, typeIndex, entity, MainEntityMgr, true);
            }
        }
    }
}
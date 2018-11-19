using System;
using System.Collections.Generic;
using System.Reflection;
using package.stormiumteam.networking.plugins;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking.ecs
{
    public enum MsgComponentOption
    {
        CreateComponentOnce,
        UpdateComponent,
        RemoveComponent
    }

    public enum PushEntityOption
    {
        CreateEntity,
        LinkEntity,
        SetOwnerLink,
        BreakOwnerLink
    }

    public partial class ConnectionEntityManagerV1 : NetworkConnectionSystem,
                                                   EventReceiveData.IEv
    {
        private EntityManager       m_MainEntityManager;
        private MsgIdRegisterSystem m_MainPatternManager;

        [Inject] private ConnectionTypeManager    m_TypeManager;
        [Inject] private ConnectionPatternManager m_ConnectionPatternManager;
        [Inject] private ConnectionMessageSystem m_MessageSystem;
        [Inject] private ConnectionUserManager m_ConnectionUserManager;

        private Dictionary<long, Entity> m_MappedEntities = new Dictionary<long, Entity>();

        public MessageIdent MsgOperationEntity = new MessageIdent()
        {
            Id      = "networkentity.operation.entity",
            Version = 0
        };

        public MessageIdent MsgOperationComponent = new MessageIdent()
        {
            Id      = "networkentity.operation.component",
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

        }

        public bool HasEntity(NetworkEntity netEntity)
        {
            return m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.NetId, netEntity.NetVersion));
        }
        
        public bool HasEntity(Entity netEntity)
        {
            if (NetInstance.SelfHost)
            {
                #if UNITY_EDITOR
                Debug.Assert(m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.Index, netEntity.Version)),
                    "m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.Index, netEntity.Version))");
                #endif
                
                return EntityManager.Exists(netEntity);
            }
            
            return m_MappedEntities.ContainsKey(StMath.DoubleIntToLong(netEntity.Index, netEntity.Version));
        }

        public Entity GetEntity(NetworkEntity netEntity)
        {
            return m_MappedEntities[StMath.DoubleIntToLong(netEntity.NetId, netEntity.NetVersion)];
        }

        public Entity GetEntity(Entity netEntity)
        {
            if (NetInstance.SelfHost)
            {
                return netEntity;
            }
            
            return m_MappedEntities[StMath.DoubleIntToLong(netEntity.Index, netEntity.Version)];
        }

        public void ServerSetEntityOwner(Entity entity, NetUser user, World world = null, CmdBuffer buffer = default(CmdBuffer))
        {
            InternalGetEntityManagerAndWorld(ref world, out var em);

            var msg = m_MessageSystem.Create(MsgOperationEntity);
            msg.Put((byte) PushEntityOption.SetOwnerLink);
            msg.Put(entity.Index);
            msg.Put(entity.Version);
            m_ConnectionUserManager.PutUserId(msg, user);

            buffer.SetOrAddComponentData(entity, new OwnedEntityData(user));
            if (user == NetInstance.NetUser) buffer.SetOrAddComponentData(entity, new WeOwnThisEntity());
            
            MainWorld.GetExistingManager<NetworkMessageSystem>().InstantSendToAll
            (
                NetInstance.GetDefaultChannel(),
                msg,
                DeliveryMethod.ReliableOrdered
            );
        }

        public void ServerBreakOwnerLink(Entity entity, World world = null, CmdBuffer buffer = default(CmdBuffer))
        {
            InternalGetEntityManagerAndWorld(ref world, out var em);
            
            var msg = m_MessageSystem.Create(MsgOperationEntity);
            msg.Put((byte) PushEntityOption.BreakOwnerLink);
            msg.Put(entity.Index);
            msg.Put(entity.Version);

            buffer.RemoveComponentIfExist<OwnedEntityData>(entity);
            buffer.RemoveComponentIfExist<WeOwnThisEntity>(entity);
            
            MainWorld.GetExistingManager<NetworkMessageSystem>().InstantSendToAll
            (
                NetInstance.GetDefaultChannel(),
                msg,
                DeliveryMethod.ReliableOrdered
            );
        }

        public void LinkNetworkEntity(Entity entityToLink, Entity    networkLink,
                                      World  world = null, CmdBuffer buffer = default(CmdBuffer))
        {
            InternalGetEntityManagerAndWorld(ref world, out var em);

            var componentToUpdate = new NetworkEntity(NetInstance.Id, networkLink);

            buffer.SetOrAddComponentData(entityToLink, componentToUpdate);

            if (em.HasComponent<Transform>(entityToLink))
            {
                var gameObject             = em.GetComponentObject<Transform>(entityToLink).gameObject;
                var referencable           = ReferencableGameObject.GetComponent<ReferencableGameObject>(gameObject);
                var networkEntityComponent = referencable.GetOrAddComponent<NetworkEntityComponent>();
                networkEntityComponent.NetId        = entityToLink.Index;
                networkEntityComponent.NetVersion   = entityToLink.Version;
                networkEntityComponent.OwnerAddress = NetInstance.World.Name;
            }

            Debug.Log("Linked entity..." + ", " + networkLink + "; " + entityToLink);
            m_MappedEntities[StMath.DoubleIntToLong(networkLink.Index, networkLink.Version)] = entityToLink;
        }

        public NetworkEntity Networkify(Entity entity, World world = null, CmdBuffer buffer = default(CmdBuffer))
        {
            InternalGetEntityManagerAndWorld(ref world, out var em);

            var id = NetInstance.Id;

            buffer.SetOrAddComponentData(entity, new NetworkEntity(id, entity));

#if UNITY_EDITOR && A != A
            if (em.HasComponent<Transform>(entity))
            {
                var gameObject             = em.GetComponentObject<Transform>(entity).gameObject;
                var referencable           = ReferencableGameObject.GetComponent<ReferencableGameObject>(gameObject);
                var networkEntityComponent = referencable.GetOrAddComponent<NetworkEntityComponent>();
                networkEntityComponent.NetId        = entity.Index;
                networkEntityComponent.NetVersion   = entity.Version;
                networkEntityComponent.OwnerAddress = NetInstance.World.Name;
            }
#endif

            return new NetworkEntity(id, entity);
        }

        public void NetworkifyAndPush(Entity entity, PushEntityOption option, CmdBuffer buffer = default(CmdBuffer), World world = null)
        {
            Networkify(entity, world);
            PushEntity(entity, option, world);
        }

        public void PushEntity(Entity entity, PushEntityOption option, World world = null)
        {
            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);

            var msg = m_MessageSystem.Create(MsgOperationEntity);
            msg.Put((byte) option);
            msg.Put(entity.Index);
            msg.Put(entity.Version);

            InternalSend(msg);
        }

        public void EntityBreakNetworkLink(Entity entity, World world = null)
        {

        }

        public void PushAllDataComponents(Entity entity, MsgComponentOption option, World world = null)
        {
            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);

            var allComponents = em.GetComponentTypes(entity);
            var length        = allComponents.Length;
            for (int i = 0; i != length; i++)
            {
                var compType = allComponents[i];
                if (typeof(IComponentData).IsAssignableFrom(compType.GetManagedType()))
                {
                    var byteArray = new byte[UnsafeUtility.SizeOf(compType.GetManagedType())];
                    
                    em.P_GetComponentDataRaw(entity, compType.TypeIndex, byteArray);
                    
                    var msg = GetMsgPushComponent(entity, option, compType.TypeIndex, byteArray, world);
                    InternalSend(msg);
                }
            }

            allComponents.Dispose();
        }

        public void SyncSetOrAddComponent<T>(Entity entity, T data, CmdBuffer buffer = default(CmdBuffer), World world = null)
            where T : struct, IComponentData
        {
            buffer.SetOrAddComponentData(entity, data);
            PushComponent<T>(entity, MsgComponentOption.UpdateComponent, data, world);
        }
        
        public void PushComponent<T>(Entity entity, MsgComponentOption option,
                                            World  world = null) where T : struct, IComponentData
        {
            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);
            
            PushComponent<T>(entity, option, em.GetComponentData<T>(entity));
        }

        public void PushComponent<T>(Entity entity, MsgComponentOption option, T data,
                                     World  world = null) where T : struct, IComponentData
        {
            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);
            CheckTypes(typeof(T));

            var msg = GetMsgPushComponent(entity, option, data, world);
            InternalSend(msg);
        }
        
        public NetDataWriter GetMsgPushComponent<T>(Entity entity, MsgComponentOption option, T data,
                                                    World  world = null) where T : struct, IComponentData
        {
            CheckTypes(typeof(T));
            
            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) option);
            msg.Put(entity);
            msg.Put(m_TypeManager.GetTypeIndex(typeof(T)));
            msg.PutBytesWithLength(UnsafeSerializer.Serialize(data)); // TODO: Find a way to allocate less (from a pool?)

            return msg;
        }
        
        public NetDataWriter GetMsgPushComponent(Entity entity, MsgComponentOption option, int typeIndex, byte[] data, World  world = null)
        {
            var managedType = TypeManager.GetType(typeIndex);
            
            CheckTypes(managedType);
            
            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) option);
            msg.Put(entity);
            msg.Put(typeIndex);
            msg.PutBytesWithLength(data);

            return msg;
        }
        
        public NetDataWriter GetMsgPushComponent<T>(Entity entity, MsgComponentOption option = MsgComponentOption.UpdateComponent,
                                                    World  world = null) where T : struct, IComponentData
        {
            CheckTypes(typeof(T));

            var data = MainEntityMgr.GetComponentData<T>(entity);
            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) option);
            msg.Put(entity);
            msg.Put(m_TypeManager.GetTypeIndex(typeof(T)));
            msg.PutBytesWithLength(UnsafeSerializer.Serialize(data)); // TODO: Find a way to allocate less (from a pool?)

            return msg;
        }

        public void SyncRemoveComponent<T>(Entity entity, T data, CmdBuffer buffer = default(CmdBuffer), World world = null)
            where T : struct , IComponentData
        {
            buffer.RemoveComponentIfExist<T>(entity);
            PullComponent<T>(entity, world);
        }
        
        public void PullComponent<T>(Entity entity, World world = null) where T : struct, IComponentData
        {
            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);
            CheckTypes(typeof(T));

            var msg = GetMsgPullComponent<T>(entity, world);
            InternalSend(msg);
        }

        public NetDataWriter GetMsgPullComponent<T>(Entity entity, World world = null) where T : struct, IComponentData
        {
            CheckTypes(typeof(T));
            
            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) MsgComponentOption.RemoveComponent);
            msg.Put(entity);
            msg.Put(m_TypeManager.GetTypeIndex(typeof(T)));

            return msg;
        }

        public void BreakNetworkLink<T>(Entity entity, World world = null)
        {
            throw new NotImplementedException();
        }

        private void InternalGetEntityManagerAndWorld(ref World world, out EntityManager em)
        {
            if (world != null)
            {
                em = world.GetExistingManager<EntityManager>();
            }
            else
            {
                world = MainWorld;
                em    = m_MainEntityManager;
            }
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
    }

    public partial class ConnectionEntityManagerV1
    {
        void EventReceiveData.IEv.Callback(EventReceiveData.Arguments args)
        {
            return;
            
            var reader       = args.Reader;
            var peerInstance = args.PeerInstance;

            if (reader.Type != MessageType.Pattern)
                return;

            var peerUserMgr = peerInstance.GetUserManager();
            var peerPatternMgr = peerInstance.GetPatternManager();
            var peerEntityMgr = peerInstance.Get<ConnectionEntityManager>();
            var peerTypeMgr = peerInstance.Get<ConnectionTypeManager>();

            var receivedPattern = peerPatternMgr.GetPattern(reader);
            if (receivedPattern == MsgOperationEntity)
            {
                var option = (PushEntityOption) reader.Data.GetByte();

                if (option == PushEntityOption.CreateEntity)
                {
                    var netIndex   = reader.Data.GetInt();
                    var netVersion = reader.Data.GetInt();

                    var netComponent = new NetworkEntity()
                    {
                        InstanceId = peerInstance.Global.Id,
                        NetId      = netIndex,
                        NetVersion = netVersion,
                    };

                    var newEntity = m_MainEntityManager.CreateEntity();
                    m_MainEntityManager.AddComponentData(newEntity, netComponent);
                    
                    LinkNetworkEntity(newEntity, netComponent.ToEntity());
                }
                else if (option == PushEntityOption.SetOwnerLink)
                {
                    var netIndex = reader.Data.GetInt();
                    var netVersion = reader.Data.GetInt();
                    var userId = peerUserMgr.GetUserId(reader);

                    var entity = new Entity {Index = netIndex, Version = netVersion};
                    if (!peerEntityMgr.HasEntity(entity))
                    {
                        Debug.LogError($"Operation SetOwnerLink: No entity with id '{netIndex}' and version '{netVersion}' found.");
                        return;
                    }
                    
                    Debug.Log($"Set owner to entity (s) {entity.Index} {entity.Version}");

                    entity = peerEntityMgr.GetEntity(entity);
                    
                    entity.SetOrAddComponentData(new OwnedEntityData(userId));

                    // Well, this is somewhat hacky and messy, but this is the easiest solution to know if we have this entity or no
                    var addWeOwnThisComponent = userId == args.Caller.NetUser;
                    
                    if (!addWeOwnThisComponent) entity.RemoveComponentIfExist<WeOwnThisEntity>();
                    else entity.SetOrAddComponentData(new WeOwnThisEntity());
                }
                else if (option == PushEntityOption.BreakOwnerLink)
                {
                    var netIndex   = reader.Data.GetInt();
                    var netVersion = reader.Data.GetInt();
                    
                    var entity = new Entity {Index = netIndex, Version = netVersion};
                    if (!peerEntityMgr.HasEntity(entity))
                    {
                        Debug.LogError($"Operation BreakOwnerLink: No entity with id '{netIndex}' and version '{netVersion}' found.");
                        return;
                    }

                    entity = peerEntityMgr.GetEntity(entity);
                    entity.RemoveComponentIfExist<OwnedEntityData>();
                    entity.RemoveComponentIfExist<WeOwnThisEntity>();
                }
            }
            else if (receivedPattern == MsgOperationComponent)
            {
                var option = (MsgComponentOption) reader.Data.GetByte();
                if (option == MsgComponentOption.UpdateComponent)
                {
                    var entity = peerEntityMgr.GetEntity(reader.Data.GetEntity());
                    var typeid = reader.Data.GetInt();
                    var dataType = peerTypeMgr.GetType(typeid);
                    var data = reader.Data.GetBytesWithLength();

                    if (!MainEntityMgr.HasComponent(entity, dataType))
                    {
                        MainEntityMgr.AddComponent(entity, dataType);
                    }
                    MainEntityMgr.P_SetComponentDataRaw(entity, dataType, data);
                }
                else if (option == MsgComponentOption.RemoveComponent)
                {
                    var entity   = peerEntityMgr.GetEntity(reader.Data.GetEntity());
                    var typeid   = reader.Data.GetInt();
                    var dataType = peerTypeMgr.GetType(typeid);
                    
                    if (MainEntityMgr.HasComponent(entity, dataType))
                    {
                        MainEntityMgr.RemoveComponent(entity, dataType);
                    }
                }
            }
        }
    }
}
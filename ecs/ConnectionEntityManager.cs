﻿using System;
using System.Collections.Generic;
using System.Reflection;
using DefaultNamespace;
using EudiFramework;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking.ecs
{
    public enum PushComponentOption
    {
        CreateComponentOnce,
        UpdateComponent,
    }

    public enum PushEntityOption
    {
        CreateEntity,
        LinkEntity,
    }

    public partial class ConnectionEntityManager : NetworkConnectionSystem,
                                                   EventReceiveData.IEv
    {
        private EntityManager       m_MainEntityManager;
        private MsgIdRegisterSystem m_MainPatternManager;

        [Inject] private ConnectionTypeManager    m_TypeManager;
        [Inject] private ConnectionPatternManager m_ConnectionPatternManager;
        [Inject] private ConnectionMessageSystem  m_MessageSystem;

        private Dictionary<NetworkEntity, Entity> m_MappedEntities = new Dictionary<NetworkEntity, Entity>();

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

        protected override void OnCreateManager(int capacity)
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
            return m_MappedEntities.ContainsKey(netEntity);
        }

        public Entity GetEntity(NetworkEntity netEntity)
        {
            return m_MappedEntities[netEntity];
        }

        public Entity GetEntity(Entity netEntity)
        {
            var component = new NetworkEntity(NetInstance.Id, netEntity);
            return m_MappedEntities[component];
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

            m_MappedEntities[componentToUpdate] = entityToLink;
        }

        public NetworkEntity Networkify(Entity entity, World world = null, CmdBuffer buffer = default(CmdBuffer))
        {
            InternalGetEntityManagerAndWorld(ref world, out var em);

            var id = NetInstance.Id;

            buffer.SetOrAddComponentData(entity, new NetworkEntity(id, entity));

            if (em.HasComponent<Transform>(entity))
            {
                var gameObject             = em.GetComponentObject<Transform>(entity).gameObject;
                var referencable           = ReferencableGameObject.GetComponent<ReferencableGameObject>(gameObject);
                var networkEntityComponent = referencable.GetOrAddComponent<NetworkEntityComponent>();
                networkEntityComponent.NetId        = entity.Index;
                networkEntityComponent.NetVersion   = entity.Version;
                networkEntityComponent.OwnerAddress = NetInstance.World.Name;
            }

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

            MainWorld.GetExistingManager<NetworkMessageSystem>().InstantSendToAll
            (
                NetInstance.GetDefaultChannel(),
                msg,
                DeliveryMethod.ReliableOrdered
            );
        }

        public void EntityBreakNetworkLink(Entity entity, World world = null)
        {

        }

        public void PushAllDataComponents(Entity entity, PushComponentOption option, World world = null)
        {
            throw new NotImplementedException();

            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);

            var allComponents = em.GetComponentTypes(entity, Allocator.Temp);
            var length        = allComponents.Length;
            for (int i = 0; i != length; i++)
            {
                var compType = allComponents[i].GetManagedType();
                if (typeof(IComponentData).IsAssignableFrom(compType))
                    PushComponent(compType, entity, option, world);
            }

            allComponents.Dispose();
        }

        public void PushComponent(Type  type, Entity entity, PushComponentOption option,
                                  World world = null)
        {
            throw new NotImplementedException();

            var method = typeof(ConnectionEntityManager).GetMethod("PushComponent");

            Assert.IsTrue(method != null, "method != null");

            var generic = method.MakeGenericMethod(type);
            generic.Invoke(this, new object[] {entity, option, world});
        }

        public void PushComponent<T>(Entity entity, PushComponentOption option,
                                     World  world = null) where T : struct, IComponentData
        {
            throw new NotImplementedException();

            EntityManager em;
            InternalGetEntityManagerAndWorld(ref world, out em);

            var data = em.GetComponentData<T>(entity);

            var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var msg = m_MessageSystem.Create(MsgOperationComponent);
            msg.Put((byte) option);
            msg.Put(typeof(T).FullName);

            //Send(msg);
        }

        public void BreakNetworkLink<T>(Entity entity, World world = null)
        {
            throw new NotImplementedException();
        }

        public void RemoveComponent<T>(Entity entity, World world = null)
        {
            throw new NotImplementedException();

            if (world != null)
            {
                var em = world.GetExistingManager<EntityManager>();
                em.RemoveComponent<T>(entity);

                BreakNetworkLink<T>(entity, world);

                return;
            }

            m_MainEntityManager.RemoveComponent<T>(entity);
            BreakNetworkLink<T>(entity);
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
    }

    public partial class ConnectionEntityManager
    {
        void EventReceiveData.IEv.Callback(EventReceiveData.Arguments args)
        {
            var reader       = args.Reader;
            var peerInstance = args.PeerInstance;

            if (reader.Type != MessageType.Pattern)
                return;

            var peerPatternManager = peerInstance.GetPatternManager();

            var receivedPattern = peerPatternManager.GetPattern(reader);
            if (receivedPattern == MsgOperationEntity)
            {
                var option = (PushEntityOption) reader.Data.GetByte();

                if (option != PushEntityOption.CreateEntity)
                    return;

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
            }
            else if (receivedPattern == MsgOperationComponent)
            {

            }
        }
    }
}
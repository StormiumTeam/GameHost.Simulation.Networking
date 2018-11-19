using System;
using System.Collections.Generic;
using LiteNetLib;
using package.stormiumteam.networking.plugins;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.ecs
{
    [UpdateAfter(typeof(ConnectionEntityManager))]
    public unsafe class ConnectionEntityDifferenceStock : NetworkConnectionSystem
    {
        internal class DictionaryValue
        {
            public ComponentShareOption Option;
            public DeliveryMethod DeliveryMethod;
            public long Pointer;
            public byte[] Data;
        }
        
        internal Dictionary<Entity, Dictionary<int, DictionaryValue>> Buffer;
        public ConnectionEntityManager ParentEntityMgr;

        private List<int> m_KeysToRemove = new List<int>();
        private List<Entity> m_EntityKeysToRemove = new List<Entity>();

        protected override void OnCreateManager()
        {
            Buffer = new Dictionary<Entity, Dictionary<int, DictionaryValue>>();
        }

        protected override void OnDestroyManager()
        {
            foreach (var buffer in Buffer)
            {
                buffer.Value.Clear();
            }
            Buffer.Clear();
            Buffer = null;
        }

        protected override void OnUpdate()
        {
            if (ParentEntityMgr == null || !ParentEntityMgr.NetInstance.SelfHost)
                return;
            if (NetInstance.SelfHost)
                return;

            foreach (var bufferEntity in Buffer)
            {
                var entity = bufferEntity.Key;

                if (!MainEntityMgr.Exists(entity))
                {
                    Debug.Log("Removing entity");

                    m_EntityKeysToRemove.Add(entity);
                    
                    continue;
                }
                    
                foreach (var component in bufferEntity.Value)
                {
                    var componentType = component.Key;
                    if (!MainEntityMgr.HasComponent(entity, TypeManager.GetType(componentType)))
                    {
                        Debug.Log("removing: " + component.Key + " from " + entity);
                        m_KeysToRemove.Add(component.Key);
                        continue;
                    }

                    var value = component.Value;

                    var currComponentPtrUnsafe = (byte*) MainEntityMgr.P_GetComponentDataRaw(entity, componentType);
                    var currComponentPtr       = new IntPtr(currComponentPtrUnsafe);
                    if (new IntPtr(value.Pointer) != currComponentPtr)
                    {
                        value.Pointer = currComponentPtr.ToInt64();
                        Debug.Log($"Update: {TypeManager.GetType(componentType).Name}");

                        var msg = ParentEntityMgr.SerializeComponent(OperationComponent.Push, entity, componentType);
                        NetInstance.PeerInstance.Peer.Send(msg, component.Value.DeliveryMethod);
                    }
                    else if (component.Value.Option == ComponentShareOption.Automatic)
                    {
                        var isEqual = true;
                        if (component.Value.DeliveryMethod != DeliveryMethod.Unreliable)
                        {
                            var size = component.Value.Data.Length;
                            fixed (byte* dataBuffer = component.Value.Data)
                            {
                                for (int i = 0; i != size; i++)
                                {
                                    if (currComponentPtrUnsafe[i] != dataBuffer[i])
                                    {
                                        isEqual = false;
                                        break;
                                    }
                                }
                                
                                UnsafeUtility.MemCpy(dataBuffer, currComponentPtrUnsafe, size);
                            }
                        }
                        else
                        {
                            isEqual = false;
                        }

                        if (!isEqual)
                        {
                            var msg = ParentEntityMgr.SerializeComponent(OperationComponent.Push, entity, componentType);
                            NetInstance.PeerInstance.Peer.Send(msg, component.Value.DeliveryMethod);
                        }
                    }
                }

                foreach (var keyToRemove in m_KeysToRemove)
                    bufferEntity.Value.Remove(keyToRemove);
                m_KeysToRemove.Clear();
            }

            foreach (var keyToRemove in m_EntityKeysToRemove)
            {
                RemEntity(keyToRemove);
            }
            m_EntityKeysToRemove.Clear();
        }

        public void AddComponent(Entity entity, int componentTypeIndex, ComponentShareOption shareOp, DeliveryMethod deliveryMethod, int ptr)
        {
            var bufferEntity = Buffer[entity];
            if (bufferEntity == null)
            {
                bufferEntity = Buffer[entity] = new Dictionary<int, DictionaryValue>();
            }

            bufferEntity[componentTypeIndex] = new DictionaryValue
            {
                Option = shareOp,
                DeliveryMethod = deliveryMethod,
                Pointer = ptr,
                Data = new byte[TypeManager.GetTypeInfo(componentTypeIndex).ElementSize]
            };
        }

        public void RemComponent(Entity entity, int componentTypeIndex)
        {
            var bufferEntity = Buffer[entity];
            if (bufferEntity == null)
                return;

            bufferEntity.Remove(componentTypeIndex);
        }

        public void AddEntity(Entity entity)
        {
            Buffer[entity] = new Dictionary<int, DictionaryValue>();
        }

        public void RemEntity(Entity entity)
        {
            if (Buffer.ContainsKey(entity))
            {
                Buffer.Remove(entity);
            }
            else
            {
                Debug.LogWarning("Inconstitency");
            }
        }

        public void SetParent(ConnectionEntityManager parent)
        {
            ParentEntityMgr = parent;
        }
    }
}
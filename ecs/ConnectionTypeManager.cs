using System;
using System.Collections.Generic;
using package.stormiumteam.networking.plugins;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.ecs
{
    public class ConnectionTypeManager : NetworkConnectionSystem,
        EventReceiveData.IEv
    {
        public static readonly MessageIdent SendAllTypesMsgId;
        
        public FastDictionary<int, ComponentType> TypeMap = new FastDictionary<int, ComponentType>();

        private int m_OldTypeCount;
        private NetDataWriter m_CachedDataWriter;

        [Inject] private ConnectionMessageSystem m_ConnectionMessageSystem;
        [Inject] private ConnectionPatternManager m_ConnectionPatternManager;
        
        protected override void OnCreateManager()
        {
            m_OldTypeCount = -1;
            
            m_CachedDataWriter = new NetDataWriter(true);
            m_CachedDataWriter.Reset(64);
            m_ConnectionPatternManager.GetRegister().Register(this);
            
            MainWorld.GetOrCreateManager<AppEventSystem>().SubscribeToAll(this);
        }

        protected override void OnUpdate()
        {
            if (NetInstance.ConnectionInfo.ConnectionType != ConnectionType.Self)
                return;

            if (RefreshTypes(false))
            {
                BroadcastNewDataToEveryone();
            }
        }

        private bool RefreshTypes(bool force)
        {
            var refresh = m_OldTypeCount != TypeManager.GetTypeCount();
            
            if (refresh || force)
            {
                m_OldTypeCount = TypeManager.GetTypeCount();

                // Broadcast all the new types
                m_CachedDataWriter.Reset(64);
                
                m_ConnectionMessageSystem.Create(SendAllTypesMsgId, m_CachedDataWriter);
                m_CachedDataWriter.Put(TypeManager.GetTypeCount());
                
                for (int i = 0; i != TypeManager.GetTypeCount(); i++)
                {
                    var type = ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(TypeManager.GetType(i)));
                    m_CachedDataWriter.Put(type.TypeIndex);
                    m_CachedDataWriter.Put(type.TypeIndex == 0 ? "null" : type.GetManagedType().AssemblyQualifiedName);
                    TypeMap[type.TypeIndex] = type;
                }
            }

            return refresh;
        }

        private void BroadcastNewDataToEveryone()
        {
            MainWorld.GetExistingManager<NetworkMessageSystem>().InstantSendToAll
            (
                NetInstance.GetDefaultChannel(),
                m_CachedDataWriter,
                DeliveryMethod.ReliableOrdered
            );
        }

        public bool HasType(int typeId)
        {
            return TypeMap.ContainsKey(typeId);
        }

        public Type GetType(int typeId)
        {
            return TypeMap[typeId].GetManagedType();
        }
        
        public int GetTypeIndex(Type type)
        {
            if (NetInstance.ConnectionInfo.ConnectionType == ConnectionType.Self)
                return TypeManager.GetTypeIndex(type);
            foreach (var kvp in TypeMap)
            {
                if (kvp.Value == type) return kvp.Key;
            }

            Debug.LogError($"No type '{type.FullName}' registered!");
            
            return -1;
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            if (RefreshTypes(true))
            {
                BroadcastNewDataToEveryone();
                return;
            }
            
            if (m_CachedDataWriter.Length == 0)
                return;
            
            peerInstance.Peer.Send(m_CachedDataWriter, DeliveryMethod.ReliableOrdered);
        }

        public void Callback(EventReceiveData.Arguments args)
        {
            var reader       = args.Reader;
            var data         = args.Reader.Data;
            var peerInstance = args.PeerInstance;
            var caller       = args.Caller;

            if (!reader.Type.IsPattern() || args.PeerInstance != NetInstance.PeerInstance) return;

            var conPatternMgr = peerInstance.GetPatternManager();
            var msgId         = conPatternMgr.GetPattern(reader);
            if (msgId == SendAllTypesMsgId)
            {
                var length = data.GetInt();
                Profiler.BeginSample("Get Types()");
                //TypeMap.Clear();
                for (int i = 0; i != length; i++)
                {
                    Type resolvedType = null;

                    var typeIndex = data.GetInt();
                    var typeName = data.GetString();
                    /*if (typeName == "null")
                        continue;*/
                    
                    Profiler.BeginSample("Resolve Type");
                    resolvedType = Type.GetType(typeName, false);
                    Profiler.EndSample();
                    
                    if (resolvedType == null && typeName != "null")
                    {
                        Debug.LogError($"Type '{typeName}' not found for us! (from peer://{peerInstance.Peer.EndPoint})");
                    }
                    else
                    {
                        //Debug.Log($"{Time.frameCount} + Type '{typeName}' added! (from peer://{peerInstance.Peer.EndPoint})");
                    }

                    Profiler.BeginSample("Register to type map");
                    TypeMap[typeIndex] = resolvedType != null ? ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(resolvedType)) : null;
                    Profiler.EndSample();
                }
                Profiler.EndSample();
            }
        }
    }
}
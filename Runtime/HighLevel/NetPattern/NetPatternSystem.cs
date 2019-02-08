using System;
using System.Collections.Generic;
using System.Text;
using package.stormiumteam.networking.runtime.highlevel;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    /// <summary>
    /// A system that give utilities for getting patterns of other network instances (or local instances).
    /// </summary>
    [UpdateAfter(typeof(UpdateLoop.IntEnd))]
    public class NetPatternSystem : NetworkComponentSystem
    {
        [Inject] private NetworkManager m_NetworkManager;

        private static PatternBank                  s_LocalBank;
        private        Dictionary<int, PatternBank> m_ConnectionsBank;
        private Dictionary<long, PatternBankExchange> m_Exchanges;
        private        NetPatternImpl               m_Impl;

        static NetPatternSystem()
        {
            s_LocalBank = new PatternBank(0);
        }

        protected override void OnCreateManager()
        {
            m_ConnectionsBank = new Dictionary<int, PatternBank>();
            m_Exchanges = new Dictionary<long, PatternBankExchange>();
            m_Impl = new NetPatternImpl
            (
                EntityManager,
                GetComponentGroup
                (
                    typeof(NetworkInstanceData),
                    typeof(QueryBuffer),
                    ComponentType.Subtractive<ValidInstanceTag>(),
                    ComponentType.Subtractive<NetworkInstanceHost>()
                ),
                GetComponentGroup
                (
                    typeof(NetworkInstanceData),
                    typeof(EventBuffer)
                ),
                GetLocalBank(),
                m_NetworkManager.InstanceValidQueryId
            );
        }

        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            var exchangeId = StMath.DoubleIntToLong(0, instanceId);
            
            m_ConnectionsBank[instanceId] = new PatternBank(instanceId);
            m_Exchanges[exchangeId] = new PatternBankExchange(exchangeId);

            var data = EntityManager.GetComponentData<NetworkInstanceData>(instanceEntity);
            // We only add queries on non local instances.
            // TODO: Maybe it would be better to add this query to LocalClient instances?
            if (!data.IsLocal())
            {
                m_Impl.OnNetworkInstanceAdded(instanceEntity);
            }
        }

        public override void OnNetworkInstanceRemoved(int instanceId, Entity instanceEntity)
        {
            m_ConnectionsBank.Remove(instanceId);
        }

        protected override void OnUpdate()
        {
            m_Impl.SendInitialMessages();
            var incomingPatterns = m_Impl.GetIncomingMessages();
            for (var i = 0; incomingPatterns != null && i != incomingPatterns.Count; i++)
            {
                var incomingPattern = incomingPatterns[i];
                var instanceBank    = GetBank(incomingPattern.InstanceId);
                var localBank = GetLocalBank();
                var exchange = GetLocalExchange(incomingPattern.InstanceId);
                
                Debug.Log($"Linking {incomingPattern.PatternResult.InternalIdent.Name} with id {incomingPattern.PatternResult.Id}");
                
                instanceBank.ForeignForceLink(incomingPattern.PatternResult);
                exchange.Set(localBank.GetPatternResult(incomingPattern.PatternResult.InternalIdent).Id, incomingPattern.PatternResult.Id);
            }
        }

        /// <summary>
        /// Get the local pattern bank
        /// </summary>
        /// <returns>Local bank</returns>
        public PatternBank GetLocalBank()
        {
            return s_LocalBank;
        }

        /// <summary>
        /// Get the pattern bank of a network instance (if it exist).
        /// </summary>
        /// <param name="instanceId">The instance id</param>
        /// <returns>The pattern bank of the instance</returns>
        public PatternBank GetBank(int instanceId)
        {
            if (instanceId == 0)
                Debug.LogError($"GetBank(0) -> Can't access to local bank here");

            return m_ConnectionsBank[instanceId];
        }

        public PatternBankExchange GetLocalExchange(int destinationInstanceId)
        {
            return GetExchange(0, destinationInstanceId);
        }

        public PatternBankExchange GetExchange(int originInstanceId, int destinationInstanceId)
        {
            return m_Exchanges[StMath.DoubleIntToLong(originInstanceId, destinationInstanceId)];
        }
    }

    /// <summary>
    /// Internal implementation for the NetPatternSystem.
    /// Mostly done for network related stuff.
    /// </summary>
    internal class NetPatternImpl : IDisposable
    {
        internal struct NewPattern
        {
            public int           InstanceId;
            public PatternResult PatternResult;

            public NewPattern(int instanceId, PatternResult patternResult)
            {
                InstanceId    = instanceId;
                PatternResult = patternResult;
            }
        }

        public EntityManager  EntityManager;
        public ComponentGroup InitInstanceGroup;
        public ComponentGroup EventInstanceGroup;
        public PatternBank    SourceBank;

        public int SendInitialPatternsQueryId;
        public int InstanceValidQueryId;

        private List<NewPattern> m_NewPatterns;

        public NetPatternImpl(EntityManager  entityManager,
                              ComponentGroup initInstanceGroup,
                              ComponentGroup eventInstanceGroup,
                              PatternBank    sourceBank,
                              int            instanceValidQueryId)
        {
            EntityManager              = entityManager;
            InitInstanceGroup          = initInstanceGroup;
            EventInstanceGroup         = eventInstanceGroup;
            SendInitialPatternsQueryId = QueryTypeManager.Create("SendInitialPatterns");
            SourceBank                 = sourceBank;
            InstanceValidQueryId       = instanceValidQueryId;

            SourceBank.PatternRegister += SourceBankOnPatternRegister;

            m_NewPatterns = new List<NewPattern>();
        }

        public void Dispose()
        {
            m_NewPatterns.Clear();

            SourceBank.PatternRegister -= SourceBankOnPatternRegister;
        }

        // TODO
        private void SourceBankOnPatternRegister(PatternResult patternResult)
        {
        }

        public void OnNetworkInstanceAdded(Entity entity)
        {
            var validatorMgr = new ValidatorManager(EntityManager, entity);
            validatorMgr.Add(SendInitialPatternsQueryId);
        }

        /// <summary>
        /// Send the initial messages that contain the local patterns to instances that don't know them (= new instances).
        /// </summary>
        public void SendInitialMessages()
        {
            var length = InitInstanceGroup.CalculateLength();
            if (length == 0) return;

            var entityArray      = InitInstanceGroup.GetEntityArray();
            var dataArray        = InitInstanceGroup.GetComponentDataArray<NetworkInstanceData>();
            var queryBufferArray = InitInstanceGroup.GetBufferArray<QueryBuffer>();
            for (var i = 0; i != length; i++)
            {
                var entity      = entityArray[i];
                var data        = dataArray[i];
                var queryBuffer = queryBufferArray[i];

                var validator = new NativeValidatorManager(queryBuffer);

                // If the instance don't have the init send pattern query, we don't send any data anymore.
                // If the instance was not validated by the network manager (if it's a foreign server),
                // we don't send any data yet (we wait...).
                if (!validator.Has(SendInitialPatternsQueryId) || validator.Has(InstanceValidQueryId))
                    continue;

                var allPatterns = SourceBank.GetResults();
                var writer      = new DataBufferWriter(SourceBank.Count * 2, Allocator.Temp);
                writer.WriteValue(MessageType.RegisterPattern);
                writer.WriteDynamicInt((ulong) SourceBank.Count);

                foreach (var result in allPatterns.Values)
                {
                    writer.WriteDynamicInt((ulong) result.Id);
                    writer.WriteStaticString(result.InternalIdent.Name);
                    writer.WriteValue<byte>(result.InternalIdent.Version);
                }

                if (!data.Commands.Send(writer, new NetworkChannel(0), Delivery.Reliable))
                {
                    Debug.LogError("Couldn't send data to " + data.InstanceType);
                }
                
                writer.Dispose();

                validator.Set(SendInitialPatternsQueryId, QueryStatus.Valid);
            }
        }

        public unsafe List<NewPattern> GetIncomingMessages()
        {
            m_NewPatterns.Clear();

            var length = EventInstanceGroup.CalculateLength();
            if (length == 0) return default(List<NewPattern>);

            var dataArray        = EventInstanceGroup.GetComponentDataArray<NetworkInstanceData>();
            var eventBufferArray = EventInstanceGroup.GetBufferArray<EventBuffer>();
            for (var i = 0; i != length; i++)
            {
                var data        = dataArray[i];
                var eventBuffer = eventBufferArray[i];

                // Process events
                for (var evIndex = 0; evIndex != eventBuffer.Length; evIndex++)
                {
                    var ev = eventBuffer[evIndex].Event;
                    // Process only 'received' event.
                    if (ev.Type != NetworkEventType.DataReceived)
                        continue;

                    var reader  = new DataBufferReader(ev.Data, ev.DataLength);
                    var msgType = reader.ReadValue<MessageType>();
                    // Process only 'RegisterPattern' messages.
                    if (msgType != MessageType.RegisterPattern)
                        continue;

                    var patternCount = (int) reader.ReadDynInteger();
                    for (var patternIndex = 0; patternIndex != patternCount; patternIndex++)
                    {
                        var id      = (int) reader.ReadDynInteger();
                        var name    = reader.ReadString();
                        var version = reader.ReadValue<byte>();

                        m_NewPatterns.Add(new NewPattern(ev.Invoker.Id, new PatternResult {Id = id, InternalIdent = new PatternIdent(name, version)}));
                    }
                }
            }

            return m_NewPatterns;
        }
    }
}
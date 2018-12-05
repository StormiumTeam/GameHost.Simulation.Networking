using System;
using System.Collections.Generic;
using package.stormiumteam.networking.runtime.highlevel;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.extensions
{        
    public class NetPatternSystem : NetworkComponentSystem
    {
        private static PatternBank s_LocalBank;
        private Dictionary<int, PatternBank> m_ConnectionsBank;

        static NetPatternSystem()
        {
            s_LocalBank = new PatternBank(0);
        }

        protected override void OnCreateManager()
        {
            m_ConnectionsBank = new Dictionary<int, PatternBank>();
        }

        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            m_ConnectionsBank[instanceId] = new PatternBank(instanceId);
        }

        public override void OnNetworkInstanceRemoved(int instanceId, Entity instanceEntity)
        {
            m_ConnectionsBank.Remove(instanceId);
        }

        protected override void OnUpdate()
        {
            
        }

        public PatternBank GetLocalBank()
        {
            return s_LocalBank;
        }

        public PatternBank GetBank(int instanceId)
        {
            if (instanceId == 0)
                Debug.LogError($"GetBank(0) -> Can't access to local bank here");

            return m_ConnectionsBank[instanceId];
        }
    }
}
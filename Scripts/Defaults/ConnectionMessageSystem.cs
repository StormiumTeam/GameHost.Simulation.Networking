using System;
using LiteNetLib.Utils;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class ConnectionMessageSystem : NetworkConnectionSystem
    {
        public const int HeaderSize = sizeof(byte)
                                      + sizeof(int)
                                      + sizeof(byte);

        [Inject] private ConnectionEventManager   m_EventManager;
        [Inject] private ConnectionPatternManager m_PatternManager;

        private NetworkMessageSystem m_NetworkMessageSystem;
        private NetDataWriter m_NetDataWriter;

        protected override void OnCreateManager(int capacity)
        {
            m_NetworkMessageSystem = MainWorld.GetOrCreateManager<NetworkMessageSystem>();
            m_NetDataWriter = new NetDataWriter(true);
        }

        protected override void OnUpdate()
        {

        }

        protected override void OnDestroyManager()
        {
            m_NetworkMessageSystem = null;
            m_PatternManager       = null;
        }

        public NetDataWriter Create(MessageIdent pattern, int initialSize = 0)
        {
            m_NetDataWriter.Reset(initialSize);
            m_NetDataWriter.Put((byte) MessageType.Pattern);
            m_PatternManager.PutPattern(m_NetDataWriter, pattern);
            return m_NetDataWriter;
        }
    }
}
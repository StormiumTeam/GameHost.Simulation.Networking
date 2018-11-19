//#define PATTERN_DEBUG

using System;
using System.Collections.Generic;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Experimental.PlayerLoop;

namespace package.stormiumteam.networking
{
    /// <summary>
    /// Manage the pattern of a connection
    /// </summary>
    [UpdateInGroup(typeof(Initialization))] // It should be one of the first manager to be run in a broadcast
    public class ConnectionPatternManager : NetworkConnectionSystem,
                                            EventReceiveData.IEv
    {
        private MsgIdRegisterSystem m_Register;

        private bool m_IsMainWorld;
        private Dictionary<string, NetDataWriter> m_MessagesWriteAddPattern = new Dictionary<string, NetDataWriter>();
        
        // -------------------------------------------------------------------- //
        // Overrides
        // -------------------------------------------------------------------- //
        protected override void OnUpdate()
        {
            
        }

        protected override void OnCreateManager()
        {
            m_IsMainWorld = NetInstance.ConnectionInfo.ConnectionType == ConnectionType.Self;
            m_Register = m_IsMainWorld
                ? MainWorld.GetOrCreateManager<MsgIdRegisterSystem>() 
                : NetWorld.GetOrCreateManager<MsgIdRegisterSystem>();

            var ev = MainWorld.GetOrCreateManager<AppEventSystem>();
            ev.SubscribeToAll(this);
            //NetworkMessageSystem.OnNewMessage += ((EventReceiveData.IEv)this).Callback;

            m_Register.OnNewPattern += (link, ident) =>
            {
                InsertPattern(ident);
            };

            foreach (var ident in m_Register.InstancePatterns.Values)
            {
                InsertPattern(ident);
            }
        }
        
        // -------------------------------------------------------------------- //
        // Events
        // -------------------------------------------------------------------- //
        void EventReceiveData.IEv.Callback(EventReceiveData.Arguments args)
        {
            args.Reader.ResetReadPosition();

            if (args.Reader.Type != MessageType.Internal
                || m_IsMainWorld)
                return;

            var intType = (InternalMessageType) args.Reader.Data.GetInt();
            if (intType == InternalMessageType.AddPattern)
            {
                var patternId = args.Reader.Data.GetString();
                var version   = args.Reader.Data.GetByte();
                var linkId    = args.Reader.Data.GetInt();

                m_Register.ForceLinkRegister(patternId, version, linkId);
            }
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            if (!peerInstance.Channel.IsMain()
                || !m_IsMainWorld)
                return;

            foreach (var msg in m_MessagesWriteAddPattern.Values)
            {
                peerInstance.Peer.Send(msg, DeliveryMethod.ReliableOrdered);
            }
        }
        
        // -------------------------------------------------------------------- //
        // Functions
        // -------------------------------------------------------------------- //

        /// <summary>
        /// Insert a pattern into the connection
        /// </summary>
        /// <param name="pattern">The pattern to insert</param>
        internal void InsertPattern(MessageIdent pattern)
        {
            if (!m_IsMainWorld)
                return;

            var linkId = m_Register.GetLinkFromIdent(pattern.Id);
            
            var dataWriter = new NetDataWriter();
            dataWriter.Put((byte) MessageType.Internal);
            dataWriter.Put((int) InternalMessageType.AddPattern);
            dataWriter.Put(pattern.Id);
            dataWriter.Put(pattern.Version);
            dataWriter.Put(linkId);
            m_MessagesWriteAddPattern[pattern.Id] = dataWriter;
        }

        public void RegisterPattern(MessageIdent pattern)
        {
            m_Register.Register(pattern);
        }

        /// <summary>
        /// Peek the pattern from the reader
        /// </summary>
        /// <param name="dataReader">The reader</param>
        /// <returns>The pattern</returns>
        public MessageIdent PeekPattern(NetDataReader dataReader)
        {
            var id      = dataReader.PeekInt();
            var version = dataReader.PeekByte();
            var pattern = m_Register.GetPatternFromLink(id);
            
            return new MessageIdent()
            {
                Id      = pattern.Id,
                Version = version
            };
        }

        /// <summary>
        /// Get the pattern from the reader
        /// </summary>
        /// <param name="dataReader">The reader</param>
        /// <returns>The pattern</returns>
        public MessageIdent GetPattern(NetDataReader dataReader)
        {
            var stringId = string.Empty;
            
            var id      = dataReader.GetInt();
            var version = dataReader.GetByte();
            
            #if PATTERN_DEBUG
            stringId = dataReader.GetString();
            #endif
            
            var pattern = m_Register.GetPatternFromLink(id);

            if (pattern == MessageIdent.Zero)
            {
                Debug.LogError("Found a zero pattern! " + stringId);
            }
            
            return new MessageIdent()
            {
                Id = pattern.Id,
                Version = version
            };
        }

        /// <summary>
        /// Peek a pattern from the reader, if the pattern is equal to the one requested, get it
        /// </summary>
        /// <param name="reader">The reader</param>
        /// <param name="equal">The pattern to compare</param>
        /// <returns>The new pattern</returns>
        public MessageIdent PeekAndGet(MessageReader reader, MessageIdent equal)
        {
            if (PeekPattern(reader).Equals(equal))
            {
                return GetPattern(reader);
            }
            return MessageIdent.Zero;
        }
        
        /// <summary>
        /// Peek the pattern from the reader
        /// </summary>
        /// <param name="reader">The reader</param>
        /// <returns>The pattern</returns>
        public MessageIdent PeekPattern(MessageReader reader)
        {
            return PeekPattern(reader.Data);
        }

        /// <summary>
        /// Get the pattern from the reader
        /// </summary>
        /// <param name="reader">The reader</param>
        /// <returns>The pattern</returns>
        public MessageIdent GetPattern(MessageReader reader)
        {
            return GetPattern(reader.Data);
        }

        /// <summary>
        /// Write a pattern into the writer
        /// </summary>
        /// <param name="dataWriter"></param>
        /// <param name="pattern"></param>
        public void PutPattern(NetDataWriter dataWriter, MessageIdent pattern)
        {
            var linkId = m_Register.GetLinkFromIdent(pattern.Id);
            dataWriter.Put(linkId);
            dataWriter.Put(pattern.Version);

#if PATTERN_DEBUG
            dataWriter.Put(pattern.Id);
#endif
        }

        public MsgIdRegisterSystem GetRegister()
        {
            return m_Register;
        }
    }
}
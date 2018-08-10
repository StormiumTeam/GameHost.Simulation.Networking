using System;
using DefaultNamespace;
using LiteNetLib;
using LiteNetLib.Utils;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class ConnectionValidateBroadcast : NetworkConnectionSystem,
        INetOnNewMessage
    {
        protected override void OnCreateManager(int capacity)
        {
            base.OnCreateManager(capacity);
            
            MainWorld.GetOrCreateManager<AppEventSystem>().SubscribeToAll(this);
        }

        protected override void OnUpdate()
        {
            
        }

        void INetOnNewMessage.Callback(NetworkInstance caller, NetPeerInstance netPeerInstance, MessageReader reader)
        {
            reader.ResetReadPosition();

            if (reader.Type != MessageType.Internal)
                return;

            var intType = (InternalMessageType) reader.Data.GetInt();

            if (intType == InternalMessageType.AllBroadcastedDataSent)
            {
                netPeerInstance.SetClientReady();
                
                netPeerInstance.AllBroadcastedDataReceived();
            }
            else if (intType == InternalMessageType.AllBroadcastedDataReceived)
            {
                netPeerInstance.SetClientReady();
            }
        }
    }
}
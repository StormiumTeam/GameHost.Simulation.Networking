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
        EventReceiveData.IEv
    {
        protected override void OnCreateManager(int capacity)
        {
            base.OnCreateManager(capacity);
            
            MainWorld.GetOrCreateManager<AppEventSystem>().SubscribeToAll(this);
        }

        protected override void OnUpdate()
        {
            
        }

        void EventReceiveData.IEv.Callback(EventReceiveData.Arguments args)
        {
            var reader = args.Reader;
            var peerInstance = args.PeerInstance;
            
            reader.ResetReadPosition();

            if (reader.Type != MessageType.Internal)
                return;

            var intType = (InternalMessageType) reader.Data.GetInt();

            if (intType == InternalMessageType.AllBroadcastedDataSent)
            {
                peerInstance.SetClientReady();
                peerInstance.AllBroadcastedDataReceived();
            }
            else if (intType == InternalMessageType.AllBroadcastedDataReceived)
            {
                peerInstance.SetClientReady();
            }
        }
    }
}
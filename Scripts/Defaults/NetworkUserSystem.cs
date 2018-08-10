using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using LiteNetLib;
using LiteNetLib.Utils;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public class NetworkUserSystem : ComponentSystem
    {
        public static event Action<NetPeerInstance, NetUser, StatusChange> OnUserEvent; 
        
        public void TriggerOnUserEvent(NetPeerInstance peerInstance, NetUser user, StatusChange change)
        {
            OnUserEvent?.Invoke(peerInstance, user, change);
        }
        
        protected override void OnUpdate()
        {
            
        }
    }
}
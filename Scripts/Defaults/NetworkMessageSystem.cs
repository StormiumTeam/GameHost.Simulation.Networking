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
    public class NetworkMessageSystem : ComponentSystem
    {
        public static event Action<NetPeerInstance, MessageReader> OnNewMessage; 
        
        public void TriggerOnNewMessage(NetPeerInstance peerInstance, MessageReader reader)
        {
            OnNewMessage?.Invoke(peerInstance, reader);
        }

        public void InstantSendTo(NetPeer peer, [CanBeNull] NetworkChannel channel, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            var peerInstance = peer.Tag as NetPeerInstance;
            
            Assert.IsTrue(peer != null, "peer != null");
            
            if (channel == null || peerInstance?.Channel == channel) peer.Send(writer, deliveryMethod);
            else
            {
                if (peerInstance == null)
                {
                    Debug.LogError("No instance for peer available.");
                    return;
                }
                
                foreach (var channelPeer in channel.Manager.ConnectedPeerList)
                {
                    if (channelPeer.Tag != peer.Tag) continue;
                    
                    channelPeer.Send(writer, deliveryMethod);
                    return;
                }
            }
        }

        public void InstantSendToAll(NetworkChannel channel, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            channel.Manager.SendToAll(writer, deliveryMethod);
        }
        
        protected override void OnUpdate()
        {
            
        }
    }
}
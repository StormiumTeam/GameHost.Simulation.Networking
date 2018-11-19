using System;
using System.Collections.Generic;
using package.stormiumteam.networking.plugins;
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
        public static event Action<NetworkInstance, NetPeerInstance, MessageReader> OnNewMessage; 
        
        public void TriggerOnNewMessage(NetworkInstance caller, NetPeerInstance peerInstance, MessageReader reader)
        {
            OnNewMessage?.Invoke(caller, peerInstance, reader);
        }

        public void InstantSendTo(NetPeer peer, [CanBeNull] NetworkChannel channel, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            var peerInstance = peer.Tag as NetPeerInstance;
            
            Assert.IsTrue(peer != null, "peer != null");
            
            if (writer.Length == 0)
                Debug.LogWarning("Zero-sized message?");
            
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
        
        public void InstantSendToAllDefault(NetworkInstance from, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            var defaultChannel = from.GetChannelManager().DefaultChannel;
         
            if (writer.Length == 0)
                Debug.LogWarning("Zero-sized message?");
            
            defaultChannel.Manager.SendToAll(writer, deliveryMethod);
        }

        public void InstantSendToAll(NetworkChannel channel, NetDataWriter writer, DeliveryMethod deliveryMethod)
        {
            if (writer.Length == 0)
                Debug.LogWarning("Zero-sized message?");
            
            channel.Manager.SendToAll(writer, deliveryMethod);
        }
        
        protected override void OnUpdate()
        {
            
        }
    }
}
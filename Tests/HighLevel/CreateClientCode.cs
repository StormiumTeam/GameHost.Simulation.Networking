using System;
using System.Net;
using ENet;
using package.stormiumteam.networking.Runtime.HighLevel;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace package.stormiumteam.networking.Tests.HighLevel
{
    public class CreateClientCode
    {
        public World ClientWorld;
        public Entity ClientInstance;
        

        public CreateClientCode(World world)
        {
            ClientWorld = world;
        }

        public void Start()
        {
            var networkMgr  = ClientWorld.GetOrCreateManager<NetworkManager>();
            var driverConf = NetDriverConfiguration.@default();
            var clientResult = networkMgr.StartClient(new IPEndPoint(IPAddress.Loopback, 9000), new IPEndPoint(IPAddress.Any, 0), driverConf);
            if (clientResult.IsError)
            {
                Debug.LogError("Error");
                return;
            }

            ClientInstance = networkMgr.GetNetworkInstanceEntity(clientResult.ClientInstanceId);
        }

        public void Update()
        {
            var em = ClientWorld.GetOrCreateManager<EntityManager>();
            var eventBuffer = em.GetBuffer<EventBuffer>(ClientInstance);
            if (eventBuffer.Length > 0)
            {
                Debug.Log("Events in queue: " + eventBuffer.Length);
            }
        }

        public void Stop()
        {
            var networkMgr = ClientWorld.GetOrCreateManager<NetworkManager>();
            networkMgr.Stop(ClientInstance);
            
            ClientInstance = Entity.Null;
        }

        public void Destroy()
        {
            if (ClientInstance == Entity.Null) return;
            
            var networkMgr = ClientWorld.GetOrCreateManager<NetworkManager>();
            networkMgr.Stop(ClientInstance);
        }
    }
}
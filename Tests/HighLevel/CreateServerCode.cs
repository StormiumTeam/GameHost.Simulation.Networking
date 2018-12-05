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
    public class CreateServerCode
    {
        public World ServerWorld;
        public Entity ServerInstance;
        

        public CreateServerCode(World world)
        {
            ServerWorld = world;
        }

        public void Start()
        {
            var networkMgr  = ServerWorld.GetOrCreateManager<NetworkManager>();
            var serveResult = networkMgr.StartServer(new IPEndPoint(IPAddress.Any, 9000), NetDriverConfiguration.@default());
            if (serveResult.IsError)
            {
                Debug.LogError("Error");
                return;
            }

            ServerInstance = networkMgr.GetNetworkInstanceEntity(serveResult.InstanceId);
        }

        public void Update()
        {
            var em = ServerWorld.GetOrCreateManager<EntityManager>();
            var eventBuffer = em.GetBuffer<EventBuffer>(ServerInstance);
            if (eventBuffer.Length > 0)
            {
                Debug.Log("Events in queue: " + eventBuffer.Length);
            }
        }

        public void Stop()
        {
            var networkMgr = ServerWorld.GetOrCreateManager<NetworkManager>();
            networkMgr.Stop(ServerInstance);
            
            ServerInstance = Entity.Null;
        }

        public void Destroy()
        {
            if (ServerInstance == Entity.Null) return;
        }
    }
} 
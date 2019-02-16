using System;
using System.Net;
using package.stormiumteam.networking.runtime.highlevel;
using package.stormiumteam.networking.runtime.lowlevel;
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
        
        [PatternName("TestPattern")]
        public PatternResult TestPattern;

        public CreateServerCode(World world)
        {
            ServerWorld = world;
        }
            
        public void Start()
        {
            var networkMgr  = ServerWorld.GetOrCreateManager<NetworkManager>();
            var serveResult = networkMgr.StartServer(new IPEndPoint(IPAddress.Parse("::0"), 9000));
            if (serveResult.IsError)
            {
                Debug.LogError("Error");
                return;
            }

            ServerInstance = networkMgr.GetNetworkInstanceEntity(serveResult.InstanceId);

            var netPatternSystem = ServerWorld.GetOrCreateManager<NetPatternSystem>();
            var patternBank      = netPatternSystem.GetLocalBank();

            patternBank.RegisterObject(this);
            
            Debug.Log($"Server Pattern: {TestPattern.Id}");
        }

        public void Update()
        {
            var em = ServerWorld.GetOrCreateManager<EntityManager>();
            var eventBuffer = em.GetBuffer<EventBuffer>(ServerInstance);
            if (eventBuffer.Length > 0)
            {
                //Debug.Log("Events in queue: " + eventBuffer.Length);
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
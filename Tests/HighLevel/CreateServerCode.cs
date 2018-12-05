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

            var instanceData = networkMgr.GetNetworkInstanceData(serveResult.InstanceId);
        }

        public void Update()
        {
            
        }

        public void Destroy()
        {
            
        }
    }
}
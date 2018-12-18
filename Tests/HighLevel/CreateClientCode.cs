using System;
using System.Net;
using ENet;
using package.stormiumteam.networking.extensions;
using package.stormiumteam.networking.runtime.highlevel;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace package.stormiumteam.networking.Tests.HighLevel
{
    public class CreateClientCode : NetEventInstanceValid.IEv
    {
        public World ClientWorld;
        public Entity ClientInstance;
        
        [PatternName("TestPattern")]
        public PatternResult TestPattern;

        public CreateClientCode(World world)
        {
            ClientWorld = world;
        }

        public void Start()
        {
            var networkMgr   = ClientWorld.GetOrCreateManager<NetworkManager>();
            var driverConf   = NetDriverConfiguration.@default();
            var clientResult = networkMgr.StartClient(new IPEndPoint(IPAddress.Loopback, 9000), new IPEndPoint(IPAddress.Any, 0), driverConf);
            if (clientResult.IsError)
            {
                Debug.LogError("Error");
                return;
            }

            ClientInstance = networkMgr.GetNetworkInstanceEntity(clientResult.ClientInstanceId);

            var netPatternSystem = ClientWorld.GetOrCreateManager<NetPatternSystem>();
            var patternBank      = netPatternSystem.GetLocalBank();

            patternBank.RegisterObject(this);
            
            Debug.Log($"Client Pattern: {TestPattern.Id}");

            var appEventSystem = ClientWorld.GetOrCreateManager<AppEventSystem>();
            appEventSystem.SubscribeToAll(this);
        }

        public void Update()
        {
            var em = ClientWorld.GetOrCreateManager<EntityManager>();
            var eventBuffer = em.GetBuffer<EventBuffer>(ClientInstance);
            var data = em.GetComponentData<NetworkInstanceData>(ClientInstance);
            if (eventBuffer.Length > 0)
            {
                //Debug.Log("Events in queue: " + eventBuffer.Length);
            }
            //Debug.Log(data.Commands.BytesReceived);
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

        public void Callback(NetEventInstanceValid.Arguments args)
        {
            if (args.World != ClientWorld) return;
            
            Debug.Log($"[Callback] Instance valid: (Id: {args.InstanceId}, Entity: {args.InstanceEntity})");
        }
    }
}
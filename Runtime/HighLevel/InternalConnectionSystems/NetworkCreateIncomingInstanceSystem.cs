﻿using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    [UpdateInGroup(typeof(UpdateLoop.IntNetworkCreateIncomingInstance))]
    public class NetworkCreateIncomingInstanceSystem : NetworkComponentSystem
    {
        private struct CreateInstance
        {
            public Entity              Entity;
            public NetworkInstanceData Data;
            public NetworkConnection   IncomingConnection;
            public NetworkCommands     IncomingConnectionCmds;
        }

        private NetworkManager m_NetworkManager;

        private EntityQuery             m_Group;
        private NativeList<CreateInstance> m_CreateInstanceList;

        protected override void OnCreate()
        {
            m_NetworkManager = World.GetOrCreateSystem<NetworkManager>();
            
            m_Group              = GetEntityQuery(typeof(NetworkInstanceData), typeof(EventBuffer));
            m_CreateInstanceList = new NativeList<CreateInstance>(Allocator.Persistent);
        }

        protected override void OnDestroy()
        {
            m_CreateInstanceList.Dispose();
        }

        protected override void OnUpdate()
        {
            m_CreateInstanceList.Clear();

            var length           = m_Group.CalculateLength();
            using (var entityArray = m_Group.ToEntityArray(Allocator.TempJob))
            using (var instanceArray = m_Group.ToComponentDataArray<NetworkInstanceData>(Allocator.TempJob))
            {
                for (var i = 0; i != length; i++)
                {
                    var instanceData = instanceArray[i];
                    if (!instanceData.IsLocal())
                        continue;

                    var entity      = entityArray[i];
                    var eventBuffer = new NativeArray<EventBuffer>(EntityManager.GetBuffer<EventBuffer>(entity).AsNativeArray(), Allocator.TempJob);

                    for (var j = 0; j != eventBuffer.Length; j++)
                    {
                        var ev = eventBuffer[j];
                        if (ev.Event.Type == NetworkEventType.Connected)
                        {
                            var newInstanceResult = m_NetworkManager.GetIncomingInstance
                            (
                                entity,
                                instanceData,
                                ev.Event.Invoker,
                                ev.Event.InvokerCmds
                            );

                            Debug.Log($"Created a new instance: (Id: {newInstanceResult.InstanceId}, Entity: {newInstanceResult.InstanceEntity})");
                        }
                        else if (ev.Event.Type == NetworkEventType.Disconnected)
                        {
                            m_NetworkManager.Stop(m_NetworkManager.GetNetworkInstanceEntity(ev.Event.Invoker.Id), false);
                        }
                    }
                    
                    eventBuffer.Dispose();
                }
            }
        }
    }
}
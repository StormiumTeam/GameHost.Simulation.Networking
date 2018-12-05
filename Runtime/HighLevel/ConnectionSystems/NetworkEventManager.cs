using System;
using package.stormiumteam.networking.Runtime.LowLevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using Event = ENet.Event;
using EventType = ENet.EventType;

#pragma warning disable 649

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public struct EventBuffer : IBufferElementData
    {
        public NetworkEvent Event;

        public EventBuffer(NetworkEvent ev)
        {
            Event = ev;
        }
    }

    public struct NewEventNotification
    {
        public int          InstanceId;
        public NetworkEvent Event;

        public NewEventNotification(int instanceId, NetworkEvent ev)
        {
            InstanceId = instanceId;
            Event      = ev;
        }
    }

    [UpdateInGroup(typeof(UpdateLoop.IntNetworkEventManager))]
    public class NetworkEventManager : NetworkComponentSystem
    {
        [Inject] private NetworkManager                m_NetworkMgr;
        [Inject] private BufferFromEntity<EventBuffer> m_EventBufferFromEntity;

        private NativeList<NewEventNotification> m_EventNotifications;

        protected override void OnCreateManager()
        {
            m_EventNotifications = new NativeList<NewEventNotification>(8, Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_EventNotifications.Dispose();
        }

        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            if (!EntityManager.HasComponent<EventBuffer>(instanceEntity))
                EntityManager.AddBuffer<EventBuffer>(instanceEntity);
        }

        protected override void OnUpdate()
        {
            m_EventNotifications.Clear();

            var manageEventJob = new ManageEventJob
            {
                EventBufferFromEntity = m_EventBufferFromEntity,
                EventNotifications    = m_EventNotifications
            };

            manageEventJob.Run(this);

            for (int i = 0; i != m_EventNotifications.Length; i++)
            {
                var notification = m_EventNotifications[i];
                Debug.Log($"{nameof(NetworkEventManager)} > {notification.Event.Type}");
            }
        }

/*#if !UNITY_EDITOR
        [BurstCompile]
#endif*/
        [RequireComponentTag(typeof(NetworkInstanceSharedData), typeof(EventBuffer))]
        private unsafe struct ManageEventJob : IJobProcessComponentDataWithEntity<NetworkInstanceData, NetworkInstanceHost>
        {
            [ReadOnly] public BufferFromEntity<EventBuffer>    EventBufferFromEntity;
            public            NativeList<NewEventNotification> EventNotifications;

            public void Execute(Entity entity, int index, [ReadOnly] ref NetworkInstanceData data, [ReadOnly] ref NetworkInstanceHost dataHost)
            {
                var eventDynBuffer = EventBufferFromEntity[entity];
                var host = dataHost.Host;

                eventDynBuffer.Clear();

                var netEvent = default(NetworkEvent);
                while (host.GetNextEvent(ref netEvent) > 0)
                {
                    Debug.Log(netEvent.Type);
                    eventDynBuffer.Add(new EventBuffer(netEvent));
                    EventNotifications.Add(new NewEventNotification(data.Id, netEvent));
                }
            }
        }
    }
}
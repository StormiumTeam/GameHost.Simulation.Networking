using System;
using ENet;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using Event = ENet.Event;

#pragma warning disable 649

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public struct EventBuffer : IBufferElementData
    {
        public Event Event;

        public EventBuffer(Event ev)
        {
            Event = ev;
        }
    }

    public struct NewEventNotification
    {
        public int   InstanceId;
        public Event Event;

        public NewEventNotification(int instanceId, Event ev)
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

        public override void OnNetworkInstanceAdded(Entity instanceEntity)
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

        [RequireComponentTag(typeof(NetworkInstanceSharedData), typeof(EventBuffer))]
        private struct ManageEventJob : IJobProcessComponentDataWithEntity<NetworkInstanceData>
        {
            public BufferFromEntity<EventBuffer>    EventBufferFromEntity;
            public NativeList<NewEventNotification> EventNotifications;

            public void Execute(Entity entity, int index, ref NetworkInstanceData data)
            {
                Debug.Assert(data.Pointer != IntPtr.Zero, "data.Pointer != 0");

                var eventDynBuffer = EventBufferFromEntity[entity];
                var nativeHost     = new NativeNetHost(data.Pointer);

                eventDynBuffer.Clear();

                Event ev;
                while (nativeHost.Service(out ev) > 0)
                {
                    eventDynBuffer.Add(new EventBuffer(ev));
                    EventNotifications.Add(new NewEventNotification(data.Id, ev));
                }
            }
        }
    }
}
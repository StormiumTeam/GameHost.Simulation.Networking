using ENet;
using package.stormiumteam.networking.runtime.lowlevel;
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

        [Inject] private NetworkManager m_NetworkManager;

        private ComponentGroup             m_Group;
        private NativeList<CreateInstance> m_CreateInstanceList;

        protected override void OnCreateManager()
        {
            m_Group              = GetComponentGroup(typeof(NetworkInstanceData), typeof(NetworkInstanceSharedData), typeof(EventBuffer));
            m_CreateInstanceList = new NativeList<CreateInstance>(Allocator.Persistent);
        }

        protected override void OnDestroyManager()
        {
            m_CreateInstanceList.Dispose();
        }

        protected override void OnUpdate()
        {
            m_CreateInstanceList.Clear();

            var length           = m_Group.CalculateLength();
            var entityArray      = m_Group.GetEntityArray();
            var instanceArray    = m_Group.GetComponentDataArray<NetworkInstanceData>();
            var eventBufferArray = m_Group.GetBufferArray<EventBuffer>();

            for (int i = 0; i != length; i++)
            {
                var instanceData = instanceArray[i];
                if (!instanceData.IsLocal())
                    continue;

                var entity      = entityArray[i];
                var eventBuffer = eventBufferArray[i];

                for (int j = 0; j != eventBuffer.Length; j++)
                {
                    var ev = eventBuffer[j];
                    if (ev.Event.Type == NetworkEventType.Connected)
                    {
                        m_CreateInstanceList.Add(new CreateInstance
                        {
                            Entity                 = entity,
                            Data                   = instanceData,
                            IncomingConnection     = ev.Event.Invoker,
                            IncomingConnectionCmds = ev.Event.InvokerCmds
                        });
                    }
                }
            }

            for (int i = 0; i != m_CreateInstanceList.Length; i++)
            {
                var create = m_CreateInstanceList[i];
                var newInstanceResult = m_NetworkManager.GetIncomingInstance
                (
                    create.Entity,
                    create.Data,
                    create.IncomingConnection,
                    create.IncomingConnectionCmds
                );

                Debug.Log($"Created a new instance: (Id: {newInstanceResult.InstanceId}, Entity: {newInstanceResult.InstanceEntity})");
            }
        }
    }
}
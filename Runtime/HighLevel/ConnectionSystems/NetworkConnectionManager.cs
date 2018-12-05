using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    [UpdateInGroup(typeof(UpdateLoop.IntNetworkConnectionManager))]
    public class NetworkConnectionManager : NetworkComponentSystem
    {
        private struct CreateClientRequest
        {
            public Entity TargetInstance;
        }

        [Inject] private BufferFromEntity<EventBuffer> m_EventBufferFromEntity;
        [Inject] private BufferFromEntity<QueryBuffer> m_QueryBufferFromEntity;

        private int             m_ClientCreatedQueryId;
        private EntityArchetype m_ClientArchetype;

        protected override void OnCreateManager()
        {
            m_ClientArchetype = EntityManager.CreateArchetype(typeof(ClientTag), typeof(ClientToNetworkInstance));

            m_ClientCreatedQueryId = QueryTypeManager.Create("ClientCreated");
        }

        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            if (EntityManager.HasComponent<NetworkInstanceToClient>(instanceEntity))
            {
                var client = EntityManager.GetComponentData<NetworkInstanceToClient>(instanceEntity);
                if (client.Target != default(Entity))
                {
                    Debug.Log($"Instance ({instanceId}) already had a client ({client.Target}).");
                    return;
                }
            }

            var instanceData = EntityManager.GetComponentData<NetworkInstanceData>(instanceEntity);
            var validatorMgr = new ValidatorManager(EntityManager, instanceEntity);
            
            validatorMgr.Add(m_ClientCreatedQueryId);

            // We only instantly validate the query on local server
            if (instanceData.InstanceType == InstanceType.LocalServer)
            {
                validatorMgr.Set(m_ClientCreatedQueryId, QueryStatus.Valid);

                var clientEntity = EntityManager.CreateEntity(m_ClientArchetype);

                EntityManager.SetComponentData(clientEntity, new ClientToNetworkInstance(instanceEntity));
                EntityManager.AddComponentData(instanceEntity, new NetworkInstanceToClient(clientEntity));

                return;
            }

            validatorMgr.Set(m_ClientCreatedQueryId, QueryStatus.Waiting);
        }

        protected override void OnUpdate()
        {
            using (var clientToCreate = new NativeList<CreateClientRequest>(16, Allocator.TempJob))
            {
                var manageConnectionJob = new ManageConnectionJob
                {
                    ClientToCreate        = clientToCreate,
                    ClientCreatedQueryId  = m_ClientCreatedQueryId,
                    EventBufferFromEntity = m_EventBufferFromEntity,
                    QueryBufferFromEntity = m_QueryBufferFromEntity
                };

                manageConnectionJob.Run(this);

                for (var i = 0; i != clientToCreate.Length; i++)
                {
                    var targetInstance = clientToCreate[i].TargetInstance;
                    var clientEntity   = EntityManager.CreateEntity(m_ClientArchetype);

                    EntityManager.SetComponentData(clientEntity, new ClientToNetworkInstance(targetInstance));
                    EntityManager.AddComponentData(targetInstance, new NetworkInstanceToClient(clientEntity));
                }
            }
        }

        [BurstCompile]
        [RequireComponentTag(typeof(NetworkInstanceSharedData), typeof(EventBuffer), typeof(QueryBuffer))]
        private struct ManageConnectionJob : IJobProcessComponentDataWithEntity<NetworkInstanceData>
        {
            public int ClientCreatedQueryId;

            public            NativeList<CreateClientRequest> ClientToCreate;
            [ReadOnly] public BufferFromEntity<EventBuffer>   EventBufferFromEntity;
            [ReadOnly] public BufferFromEntity<QueryBuffer>   QueryBufferFromEntity;

            public void Execute(Entity entity, int index, ref NetworkInstanceData data)
            {
                var eventBuffer = EventBufferFromEntity[entity];
                var queryBuffer = QueryBufferFromEntity[entity];

                var validatorMgr = new NativeValidatorManager(queryBuffer);
                for (var i = 0; i != eventBuffer.Length; i++)
                {
                    var ev = eventBuffer[i].Event;
                    if (data.Id != ev.Invoker.Id || ev.Type != NetworkEventType.Connected) continue;

                    // If we have the right peer and it's asking for a connection, create a new client request
                    ClientToCreate.Add(new CreateClientRequest
                    {
                        TargetInstance = entity
                    });

                    validatorMgr.Set(ClientCreatedQueryId, QueryStatus.Valid);
                }
            }
        }
    }
}
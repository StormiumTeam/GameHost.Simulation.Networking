using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public struct ValidInstanceTag : IComponentData
    {
        
    }
    
    [UpdateAfter(typeof(UpdateLoop.IntNetworkValidateInstance))]
    public class NetworkValidateInstances : NetworkComponentSystem
    {
        [Inject] private NetworkInternalEnd            End;
        [Inject] private BufferFromEntity<QueryBuffer> m_QueryBufferFromEntity;

        private ComponentGroup m_Group;
        
        protected override void OnCreateManager()
        {
            m_Group = GetComponentGroup(typeof(NetworkInstanceData), typeof(QueryBuffer), ComponentType.Subtractive<ValidInstanceTag>());
        }

        protected override void OnUpdate()
        {
            var validateJob = new ValidateJob
            {
                QueryBufferFromEntity = m_QueryBufferFromEntity
            };

            validateJob.Run(this);

            var length           = m_Group.CalculateLength();
            var queryBufferArray = m_Group.GetBufferArray<QueryBuffer>();
            var instanceArray    = m_Group.GetComponentDataArray<NetworkInstanceData>();
            var entityArray      = m_Group.GetEntityArray();
            for (var i = 0; i != length; i++)
            {
                var queryBuffer = queryBufferArray[i];
                var data        = instanceArray[i];
                var entity      = entityArray[i];
                if (queryBuffer.Length == 0)
                {
                    PostUpdateCommands.AddComponent(entity, new ValidInstanceTag());

                    var args = new NetEventInstanceValid.Arguments(World, data.Id, entity);
                    var objArray = AppEvent<NetEventInstanceValid.IEv>.GetObjEvents();
                    foreach (var obj in objArray)
                    {
                        obj.Callback(args);
                    }
                }
                else
                {
                    for (int j = 0; j != queryBuffer.Length; j++)
                    {
                        var query = queryBuffer[j];
                        Debug.Log($"[{World.Name}] {entity}, Missing: {QueryTypeManager.GetName(query.Type)}");
                    }
                }
            }
        }

        [BurstCompile]
        [RequireComponentTag(typeof(QueryBuffer))]
        public struct ValidateJob : IJobProcessComponentDataWithEntity<NetworkInstanceData>
        {
            [ReadOnly] public BufferFromEntity<QueryBuffer> QueryBufferFromEntity;

            public void Execute(Entity entity, int index, ref NetworkInstanceData data)
            {
                var queryBuffer = QueryBufferFromEntity[entity];
                if (queryBuffer.Length == 0)
                    return;

                // Remove by a swapback method
                for (var i = 0; i != queryBuffer.Length; i++)
                {
                    if ((queryBuffer[i].Status & QueryStatus.Valid) == 0) continue;

                    queryBuffer.RemoveAt(i);
                    i--;
                }
            }
        }
    }
}
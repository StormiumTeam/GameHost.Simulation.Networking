using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    [UpdateAfter(typeof(UpdateLoop.IntNetworkValidateInstance))]
    public class NetworkValidateInstances : NetworkComponentSystem
    {
        [Inject] private BufferFromEntity<QueryBuffer> m_QueryBufferFromEntity;
        
        protected override void OnUpdate()
        {
            var validateJob = new ValidateJob
            {
                QueryBufferFromEntity = m_QueryBufferFromEntity
            };
            
            validateJob.Run(this);
        }
        
        [RequireComponentTag(typeof(QueryBuffer))]
        public struct ValidateJob : IJobProcessComponentDataWithEntity<NetworkInstanceData>
        {
            public BufferFromEntity<QueryBuffer> QueryBufferFromEntity;
            
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
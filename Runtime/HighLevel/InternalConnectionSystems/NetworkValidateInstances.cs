using package.stormiumteam.shared;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public struct ValidInstanceTag : IComponentData
    {
        
    }
    
    [UpdateAfter(typeof(UpdateLoop.IntNetworkValidateInstance))]
    public class NetworkValidateInstances : NetworkComponentSystem
    {
        private EntityQuery m_Group;
        
        protected override void OnCreate()
        {
            m_Group = GetEntityQuery(typeof(NetworkInstanceData), typeof(QueryBuffer), ComponentType.Exclude<ValidInstanceTag>());
        }

        protected override void OnUpdate()
        {
            var length           = m_Group.CalculateLength();
            var instanceArray    = m_Group.ToComponentDataArray<NetworkInstanceData>(Allocator.TempJob);
            var entityArray      = m_Group.ToEntityArray(Allocator.TempJob);
            for (var i = 0; i != length; i++)
            {
                var data        = instanceArray[i];
                var entity      = entityArray[i];
                var queryBuffer = EntityManager.GetBuffer<QueryBuffer>(entity);

                for (var j = 0; j != queryBuffer.Length; j++)
                {
                    if ((queryBuffer[j].Status & QueryStatus.Valid) == 0) continue;
                    
                    // Remove by a swapback method
                    queryBuffer.RemoveAt(j);
                    j--;
                }
                
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
            }
            
            instanceArray.Dispose();
            entityArray.Dispose();
        }
    }
}
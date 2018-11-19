using package.stormiumteam.networking.plugins;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Experimental.PlayerLoop;

namespace package.stormiumteam.networking.ecs
{
    [UpdateBefore(typeof(Update))]
    public class BroadcastEntityComponentsSystem : ComponentSystem
    {
        struct Group
        {
            public ComponentDataArray<BroadcastEntityComponentsOnce>            Data;
            public SubtractiveComponent<BroadcastEntityComponentsThenDestroyIt> Void1;
            public EntityArray                                                  Entities;

            public readonly int Length;
        }

        struct GroupWithDestroy
        {
            public ComponentDataArray<BroadcastEntityComponentsOnce>          Data;
            public ComponentDataArray<BroadcastEntityComponentsThenDestroyIt> Tag1;
            public EntityArray                                                Entities;
            
            public readonly int Length;
        }

        [Inject] private Group            m_Group;
        [Inject] private GroupWithDestroy m_GroupWithDestroy;

        protected override void OnUpdate()
        {
            for (int i = 0; i != m_Group.Length; i++)
            {
                var data = m_Group.Data[i];
                if (data.NetInstanceId >= 0)
                {
                    var instance     = NetworkInstance.FromId(data.NetInstanceId);
                    if (instance.ConnectionInfo.ConnectionType == ConnectionType.Self)
                    {
                        var conEntityMgr = instance.Get<ConnectionEntityManager>();

                        conEntityMgr.ConvertAsNetworkable(PostUpdateCommands, m_Group.Entities[i], m_Group.Entities[i]);
                        //conEntityMgr.PushAllDataComponents(m_GroupWithDestroy.Entities[i], MsgComponentOption.UpdateComponent);
                    }
                }
            }

            for (int i = 0; i != m_GroupWithDestroy.Length; i++)
            {
                var data         = m_GroupWithDestroy.Data[i];
                if (data.NetInstanceId >= 0)
                {
                    var instance = NetworkInstance.FromId(data.NetInstanceId);
                    if (instance.ConnectionInfo.ConnectionType == ConnectionType.Self)
                    {
                        var conEntityMgr = instance.Get<ConnectionEntityManager>();
                        conEntityMgr.ConvertAsNetworkable(PostUpdateCommands, m_GroupWithDestroy.Entities[i], m_GroupWithDestroy.Entities[i]);
                        //conEntityMgr.PushAllDataComponents(m_GroupWithDestroy.Entities[i], MsgComponentOption.UpdateComponent);
                    }
                }

                Debug.Log("Destroyed networked entity");
                
                PostUpdateCommands.DestroyEntity(m_GroupWithDestroy.Entities[i]);
            }
        }
    }
}
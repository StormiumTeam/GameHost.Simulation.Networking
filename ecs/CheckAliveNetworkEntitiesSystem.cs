using LiteNetLib;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.ecs
{
    public class CheckAliveNetworkEntitiesSystem : ComponentSystem
    {
        public struct Group
        {
            public ComponentDataArray<NetworkEntity>                                 NetworkEntityArray;
            public SubtractiveComponent<NetworkEntityDontDestroyOnHostDisconnectTag> DontDestroyTag;
            public EntityArray                                                       Entities;

            public readonly int Length;
        }

        [Inject] private Group m_Group;

        protected override void OnUpdate()
        {
            for (int i = 0; i != m_Group.Length; i++)
            {
                var networkEntity   = m_Group.NetworkEntityArray[i];
                var networkInstance = networkEntity.GetNetworkInstance();
                if (networkInstance == null)
                {
                    // Destroy entity
                    if (EntityManager.HasComponent<Transform>(m_Group.Entities[i]))
                    {
                        var tr = EntityManager.GetComponentObject<Transform>(m_Group.Entities[i]);
                        Object.Destroy(tr.gameObject);
                    }
                    else
                    {
                        PostUpdateCommands.DestroyEntity(m_Group.Entities[i]);
                    }
                }
            }
        }
    }
}
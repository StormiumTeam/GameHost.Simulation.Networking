using System.Collections;
using System.Linq;
using ENet;
using package.stormiumteam.shared.utils;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.Tests.HighLevel
{
    public class HighLevelNetTest : MonoBehaviour
    {
        private CreateServerCode m_ServerCode;
        private CreateClientCode m_ClientCode;
        private World m_ServerWorld;
        private World m_ClientWorld;

        private void Start()
        {
            if (!Library.Initialize()) Debug.LogWarning("Couldn't initialize ENet");
            
            m_ServerWorld = new World("Server");
            m_ClientWorld = new World("Client");

            // Copy the systems into our new worlds
            foreach (var sbm in World.Active.BehaviourManagers)
            {
                m_ServerWorld.GetOrCreateManager(sbm.GetType());
                m_ClientWorld.GetOrCreateManager(sbm.GetType());
            }
            
            m_ServerCode = new CreateServerCode(m_ServerWorld);
            m_ClientCode = new CreateClientCode(m_ClientWorld);
            
            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.AllWorlds.ToArray());
            
            m_ServerCode.Start();
            m_ClientCode.Start();
        }

        private void Update()
        {
            m_ServerCode.Update();
            m_ClientCode.Update();

            /*Profiler.BeginSample("Create worlds");
            var defaultWorldEm = World.Active.GetExistingManager<EntityManager>();
            defaultWorldEm.CreateEntity();
            for (int i = 0; i != 4; i++)
            {
                using (var worldPoolItem = WorldPool.Get())
                {
                    var worldEm = worldPoolItem.EntityManager;
                    var archetype = worldEm.CreateArchetype();
                    var ex = worldEm.BeginExclusiveEntityTransaction();
                    var e = ex.CreateEntity(typeof(TestTransactionTarget));
                    ex.SetComponentData(e, new TestTransactionTarget {target = e});
                    
                    worldEm.EndExclusiveEntityTransaction();
                    defaultWorldEm.MoveEntitiesFrom(worldEm);
                }
            }
            Profiler.EndSample();*/
        }

        public struct TestTransactionTarget : IComponentData
        {
            public Entity target;
        }

        private void OnGUI()
        {
            GUILayout.BeginVertical();
            if (m_ServerCode.ServerInstance != Entity.Null)
            {
                if (GUILayout.Button("Stop"))
                    m_ServerCode.Stop();
            }
            else
            {
                if (GUILayout.Button("Start"))
                    m_ServerCode.Start();
            }

            if (GUILayout.Button("DISPOSE ALL"))
            {
                m_ServerWorld.Dispose();
                m_ClientWorld.Dispose();
            }

            GUILayout.EndVertical();
        }

        private void OnDestroy()
        {            
            Library.Deinitialize();
        }
    }
} 
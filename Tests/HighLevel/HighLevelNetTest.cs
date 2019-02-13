using System.Collections;
using System.Linq;
using package.stormiumteam.networking.runtime.lowlevel;
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
            // Server
            if (Application.isEditor)
            {
                m_ServerWorld = new World("Server");
                
                foreach (var sbm in World.Active.BehaviourManagers)
                {
                    m_ServerWorld.GetOrCreateManager(sbm.GetType());
                }
                
                m_ServerCode = new CreateServerCode(m_ServerWorld);
                m_ServerCode.Start(); 

            }
            else
            {
                m_ClientWorld = new World("Client");

                // Copy the systems into our new worlds
                foreach (var sbm in World.Active.BehaviourManagers)
                {
                    m_ClientWorld.GetOrCreateManager(sbm.GetType());
                }

                m_ClientCode = new CreateClientCode(m_ClientWorld);
                m_ClientCode.Start();
            }

            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.AllWorlds.ToArray());
        }

        private void Update()
        {
            if (Application.isEditor)
            {
                m_ServerCode.Update();
            }
            else
            {
                m_ClientCode.Update();
            }
        }

        private void OnGUI()
        {
            return;
            
            GUILayout.BeginVertical();
            if (m_ServerCode.ServerInstance != Entity.Null)
            {
                if (GUILayout.Button("Stop Server"))
                {
                    m_ServerCode.Stop();
                }
            }
            else
            {
                if (GUILayout.Button("Start Server"))
                {
                    m_ServerCode.Start();
                }
            }
            
            if (m_ClientCode.ClientInstance != Entity.Null)
            {
                if (GUILayout.Button("Stop Client"))
                {
                    m_ClientCode.Stop();
                }
            }
            else
            {
                if (GUILayout.Button("Start Client"))
                {
                    m_ClientCode.Start();
                }
            }

            if (GUILayout.Button("DISPOSE ALL"))
            {
                m_ServerWorld.Dispose();
                m_ClientWorld.Dispose();
            }

            GUILayout.EndVertical();
        }
    }
} 
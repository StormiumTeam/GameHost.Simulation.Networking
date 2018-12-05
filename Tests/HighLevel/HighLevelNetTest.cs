using System.Linq;
using ENet;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Playables;

namespace package.stormiumteam.networking.Tests.HighLevel
{
    public class HighLevelNetTest : MonoBehaviour
    {
        private CreateServerCode m_ServerCode;
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
            
            ScriptBehaviourUpdateOrder.UpdatePlayerLoop(World.AllWorlds.ToArray());
            
            m_ServerCode.Start();
        }

        private void Update()
        {   
            m_ServerCode.Update();
        }

        private void OnDestroy()
        {
            m_ServerCode.Destroy();
            
            m_ServerWorld.Dispose();
            m_ClientWorld.Dispose();
            
            Library.Deinitialize();
        }
    }
}
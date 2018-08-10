using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class NetworkEventSystem : ComponentSystem
    {
        [Inject] private AppEventSystem m_AppEventSystem;
        
        protected override void OnCreateManager(int capacity)
        {
            base.OnCreateManager(capacity);

            NetworkInstance.OnInstanceReady += EventOnInstanceReady;
            NetworkMessageSystem.OnNewMessage += EventOnNewMessage;
            NetworkUserSystem.OnUserEvent += EventOnUserEvent;
        }

        protected override void OnUpdate()
        {
        }
        
        private void EventOnInstanceReady(NetworkInstance networkInstance, ConnectionType connectionType)
        {
            m_AppEventSystem.CheckLoopValidity();

            foreach (var manager in AppEvent<INetOnInstanceReady>.eventList)
            {
                manager.Callback(networkInstance, connectionType);
            }
        }

        private void EventOnNewMessage(NetworkInstance caller, NetPeerInstance netPeerInstance, MessageReader reader)
        {
            m_AppEventSystem.CheckLoopValidity();
            
            foreach (var manager in AppEvent<INetOnNewMessage>.eventList)
            {
                reader.ResetReadPosition();
                manager.Callback(caller, netPeerInstance, reader);
            }
            reader.ResetReadPosition();
        }

        private void EventOnUserEvent(NetPeerInstance holder, NetUser user, StatusChange change)
        {
            m_AppEventSystem.CheckLoopValidity();
            
            foreach (var manager in AppEvent<INetOnUserStatusChange>.eventList)
            {
                manager.Callback(holder, user, change);
            }
        }
    }
}
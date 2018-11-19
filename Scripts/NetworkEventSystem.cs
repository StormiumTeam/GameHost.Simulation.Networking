using System;
using package.stormiumteam.shared;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public class NetworkEventSystem : ComponentSystem
    {
        [Inject] private AppEventSystem m_AppEventSystem;
        
        protected override void OnCreateManager()
        {
            base.OnCreateManager();

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

            foreach (var manager in AppEvent<EventInstanceReady.IEv>.eventList)
            {
                manager.Callback(new EventInstanceReady.Arguments(networkInstance, connectionType));
            }
        }

        private void EventOnNewMessage(NetworkInstance caller, NetPeerInstance netPeerInstance, MessageReader reader)
        {
            m_AppEventSystem.CheckLoopValidity();

            foreach (var manager in AppEvent<EventReceiveData.IEv>.eventList)
            {
                reader.ResetReadPosition();
#if UNITY_EDITOR || DEBUG
                try
                {
#endif
                    manager.Callback(new EventReceiveData.Arguments(caller, netPeerInstance, reader));
#if UNITY_EDITOR || DEBUG
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
#endif
            }

            reader.ResetReadPosition();
        }

        private void EventOnUserEvent(NetPeerInstance holder, NetUser user, StatusChange change)
        {
            m_AppEventSystem.CheckLoopValidity();
            
            foreach (var manager in AppEvent<EventUserStatusChange.IEv>.eventList)
            {
                manager.Callback(new EventUserStatusChange.Arguments(holder, user, change));
            }
        }
    }
}
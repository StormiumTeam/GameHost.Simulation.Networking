using System;
using System.Collections.Generic;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared.utils;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using Valve.Sockets;

#pragma warning disable 649

namespace package.stormiumteam.networking.runtime.highlevel
{
    public struct EventBuffer : IBufferElementData
    {
        public NetworkEvent Event;

        public EventBuffer(NetworkEvent ev)
        {
            Event = ev;
        }
    }

    public struct NewEventNotification
    {
        public int          InstanceId;
        public NetworkEvent Event;

        public NewEventNotification(int instanceId, NetworkEvent ev)
        {
            InstanceId = instanceId;
            Event      = ev;
        }
    }

    [UpdateInGroup(typeof(UpdateLoop.IntNetworkEventManager))]
    public unsafe class NetworkEventManager : NetworkComponentSystem
    {
        [Inject] private NetworkManager                m_NetworkMgr;
        [Inject] private BufferFromEntity<EventBuffer> m_EventBufferFromEntity;

        private NativeList<NewEventNotification> m_EventNotifications;
        private ComponentGroup m_Group;

        protected override void OnCreateManager()
        {
            m_EventNotifications = new NativeList<NewEventNotification>(8, Allocator.Persistent);
            m_RunConnectionCallbacks = new List<StatusInfo>();
            m_Group = GetComponentGroup(typeof(NetworkInstanceData), typeof(NetworkInstanceHost));
        }

        protected override void OnDestroyManager()
        {
            m_EventNotifications.Dispose();
            m_RunConnectionCallbacks.Clear();
            m_RunConnectionCallbacks = null;
        }

        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            var instanceData = EntityManager.GetComponentData<NetworkInstanceData>(instanceEntity);
            if (!EntityManager.HasComponent<EventBuffer>(instanceEntity) && instanceData.IsLocal())
                EntityManager.AddBuffer<EventBuffer>(instanceEntity);
        }

        private void AddMessagesAsEvent(NmLkSpan<NetworkingMessage> messages, DynamicBuffer<EventBuffer> evBuffer, GnsExecution execution)
        {
            for (var i = 0; i != messages.Length; i++)
            {   
                var msg = messages[i];
                var id = execution.GetConnectionData(msg.connection);
                if (!NativeConnection.TryGet(id, out var peerConnection))
                {
                    Debug.LogError($"No NativeConnection found for id={msg.connection} ({msg.userData}) ({msg.identity.type}) ({msg.messageNumber}) ({msg.length})");
                    continue;
                }
                
                //Debug.Log($"Received a message from NativeConnection={msg.connection}, peerId={peerConnection.Connection.ToString()}, l={msg.length}");
                
                var netCmd = NetworkCommands.CreateFromConnection(execution.NativePtr, msg.connection);
                var ev = new NetworkEvent(NetworkEventType.DataReceived, peerConnection.Connection, netCmd);

                var cpyData = UnsafeUtility.Malloc(msg.length, UnsafeUtility.AlignOf<NetworkingMessage>(), Allocator.TempJob);
                UnsafeUtility.MemCpy(cpyData, (void*) msg.data, msg.length);
                ev.SetData((byte*) cpyData, msg.length);

                msg.release = messages.Pointer + (UnsafeUtility.SizeOf<NetworkingMessage>() * i);
                msg.Destroy();
                
                evBuffer.Add(new EventBuffer(ev));
            }
        }

        private List<StatusInfo> m_RunConnectionCallbacks;
        
        private void OnCallback(StatusInfo statusInfo, IntPtr ctx)
        {
            m_RunConnectionCallbacks.Add(statusInfo);
        }

        protected override void OnUpdate()
        {
            m_EventNotifications.Clear();
            
            var networkMgr = World.GetExistingManager<NetworkManager>();

            using (var entityArray = m_Group.ToEntityArray(Allocator.TempJob))
            using (var dataArray = m_Group.ToComponentDataArray<NetworkInstanceData>(Allocator.TempJob))
            using (var hostArray = m_Group.ToComponentDataArray<NetworkInstanceHost>(Allocator.TempJob))
            {
                for (var i = 0; i != entityArray.Length; i++)
                {
                    var entity = entityArray[i];
                    var data   = dataArray[i];
                    var host   = hostArray[i];
                    var evBuffer = m_EventBufferFromEntity[entity];
                    
                    var nativePtr = host.Host.NativePtr;
                    var execution = new GnsExecution(nativePtr, host.Host.Socket);

                    var hostConnection = host.Host.HostConnection;
                    var foreignConnection = default(NetworkConnection);

                    // This is seriously ugly and a big hack, but we need to do that
                    // as the GNS api will reset our peer at first connection (problematic for Client To Server relation).
                    var pendingConnections = networkMgr.UglyPendingServerConnections;

                    // Clear previous data from buffer
                    for (var j = 0; j != evBuffer.Length; j++)
                    {
                        var ev = evBuffer[j].Event;
                        if (ev.Type == NetworkEventType.DataReceived)
                        {
                            UnsafeUtility.Free(ev.Data, Allocator.TempJob);
                        }
                    }

                    m_RunConnectionCallbacks.Clear();
                    evBuffer.Clear();
                    
                    NmLkSpan<NetworkingMessage> messages;
                    
                    execution.RunConnectionStatusCallback(OnCallback, IntPtr.Zero);

                    for (var j = 0; j != m_RunConnectionCallbacks.Count; j++)
                    {
                        var info = m_RunConnectionCallbacks[j];

                        NativeConnection peerConnection;

                        if (!pendingConnections.ContainsKey(info.connection))
                        {
                            if (!NativeConnection.GetOrCreate(nativePtr, info.connection, out peerConnection))
                            {
                                Debug.Log("We allocated a new NativeCollection for #" + info.connection);
                            }

                            execution.SetConnectionData(info.connection, foreignConnection.Id);
                        }
                        else
                        {
                            peerConnection = pendingConnections[info.connection];

                            //pendingConnections.Remove(info.connection);
                        }

                        if (peerConnection.IsCreated)
                        {
                            foreignConnection          = peerConnection.Connection;
                            foreignConnection.ParentId = hostConnection.Id;
                            peerConnection.Connection  = foreignConnection;
                        }

                        var newNetEvent = new NetworkEvent
                        {
                            Invoker     = foreignConnection,
                            InvokerCmds = NetworkCommands.CreateFromConnection(nativePtr, info.connection)
                        };

                        //
                        execution.SetConnectionData(info.connection, foreignConnection.Id);

                        switch (info.connectionInfo.state)
                        {
                            // For now accept any connection.
                            case ConnectionState.Connecting:
                            {
                                newNetEvent.Type = NetworkEventType.Connecting;

                                if (data.InstanceType == InstanceType.LocalServer)
                                {
                                    var result = execution.AcceptConnection(info.connection);
                                    if (result != Result.OK)
                                    {
                                        Debug.LogError($"execution.AcceptConnection#{info.connection}<{execution.NativePtr}>={result} (expected={Result.OK})");

                                        execution.CloseConnection(info.connection);
                                    }

                                    Debug.Log("Accepted.");
                                }
                                else
                                {
                                    Debug.Log("Ignored.");
                                }

                                break;
                            }
                            case ConnectionState.Connected:
                            {
                                newNetEvent.Type = NetworkEventType.Connected;

                                Debug.Log($"connection#{info.connection} has connected!");
                                break;
                            }

                            case ConnectionState.ClosedByPeer:
                            {
                                newNetEvent.Type = NetworkEventType.Disconnected;

                                if (!execution.CloseConnection(info.connection))
                                    Debug.LogError($"execution.CloseConnection#{info.connection}=false (expected=true)");

                                break;
                            }
                        }

                        evBuffer.Add(new EventBuffer(newNetEvent));
                    }

                    if ((data.InstanceType & InstanceType.Server) != 0)
                    {
                        // This is ugly, but we need to limit the rate to one message per function
                        // Or else we will get strange bug when we will read the second message.
                        while ((messages = execution.ReceiveMessageOnListenSocket()).Length > 0)
                        {
                            AddMessagesAsEvent(messages, evBuffer, execution);
                        }
                    }
                    else
                    {
                        var serverConnection = EntityManager.GetComponentData<NetworkInstanceData>(data.Parent).GnsConnectionId;
                        // This is ugly, but we need to limit the rate to one message per function
                        // Or else we will get strange bug when we will read the second message.
                        while ((messages = execution.ReceiveMessageOnConnection(serverConnection)).Length > 0)
                        {
                            AddMessagesAsEvent(messages, evBuffer, execution);
                        }
                    }
                }
            }
        }
    }
}
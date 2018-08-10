using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using package.stormiumteam.networking;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Assertions;

namespace package.stormiumteam.networking
{
    public partial class NetworkManager : ComponentSystem
    {
        // all connections to our network (clients into a server)
        private List<NetworkInstance> m_InInstances;

        // all connections from our network (servers into a client)
        private List<NetworkInstance> m_OutInstances;

        // all owned connections that can receive clients
        private List<NetworkInstance> m_SelfInstances;

        /// <summary>
        /// All connections to our main instance
        /// </summary>
        public ReadOnlyCollection<NetworkInstance> In { get; private set; }

        /// <summary>
        /// All connections made to another instance
        /// </summary>
        public ReadOnlyCollection<NetworkInstance> Out { get; private set; }

        /// <summary>
        /// The main instance, if we host to receive connections (there "should" be only one)
        /// </summary>
        public ReadOnlyCollection<NetworkInstance> Self { get; private set; }

        protected override void OnCreateManager(int capacity)
        {
            m_InInstances   = new List<NetworkInstance>();
            m_OutInstances  = new List<NetworkInstance>();
            m_SelfInstances = new List<NetworkInstance>();

            In   = new ReadOnlyCollection<NetworkInstance>(m_InInstances);
            Out  = new ReadOnlyCollection<NetworkInstance>(m_OutInstances);
            Self = new ReadOnlyCollection<NetworkInstance>(m_SelfInstances);
        }

        protected override void OnUpdate()
        {

        }

        protected override void OnDestroyManager()
        {
            foreach (var instance in m_InInstances)
                instance.Dispose();
            foreach (var instance in m_OutInstances)
                instance.Dispose();
            foreach (var instance in m_SelfInstances)
                instance.Dispose();

            m_InInstances.Clear();
            m_OutInstances.Clear();
            m_SelfInstances.Clear();
            m_InInstances   = null;
            m_OutInstances  = null;
            m_SelfInstances = null;

            In   = null;
            Out  = null;
            Self = null;
        }

        public void AddInstance(NetworkInstance instance, ConnectionType type, NetworkInstance interParent = null, bool finalize = true, bool setReady = true)
        {
            Assert.IsFalse(instance == null, "instance == null");
            Assert.IsFalse(instance?.World == null, "instance.World == null");
            
            switch (type)
            {
                case ConnectionType.In:
                    m_InInstances.Add(instance);
                    break;
                case ConnectionType.Out:
                    m_OutInstances.Add(instance);
                    break;
                case ConnectionType.Self:
                    m_SelfInstances.Add(instance);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
            
            Debug.Log($"Adding a new <b>'{type}'</b> network instance");

            instance.World.CreateSystems();
            
            if (finalize) instance.FinalizeInstance();
            if (setReady) instance.SetReady();
            
            if (interParent != null) interParent.m_Interconnections.Add(instance);
        }

        public void RemoveInstance(NetworkInstance instance)
        {
            Assert.IsTrue(instance != null, "instance != null");

            var type = instance.ConnectionInfo.ConnectionType;
            switch (type)
            {
                case ConnectionType.In:
                    m_InInstances.Remove(instance);
                    break;
                case ConnectionType.Out:
                    m_OutInstances.Remove(instance);
                    break;
                case ConnectionType.Self:
                    m_SelfInstances.Remove(instance);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public NetworkInstance CreateOutConnection(IOutConnectionCreator connectionCreator)
        {
            var netInstance = new NetworkInstance(new NetworkConnectionInfo()
            {
                ConnectionType = ConnectionType.Out,
                Creator        = connectionCreator
            }, true, true);

            connectionCreator.Execute(netInstance);

            AddInstance(netInstance, ConnectionType.Out);

            return netInstance;
        }

        public NetworkInstance CreateSelfConnection(ISelfConnectionCreator connectionCreator)
        {
            connectionCreator.Init();
            
            var netInstance = new NetworkInstance(new NetworkConnectionInfo()
            {
                ConnectionType = ConnectionType.Self,
                Creator        = connectionCreator
            }, true, true);

            connectionCreator.Execute(netInstance);

            AddInstance(netInstance, ConnectionType.Self);

            return netInstance;
        }
    }
}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Entities;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public interface INetworkComponentSystem
    {
        void OnNetworkInstanceAdded(Entity instanceEntity);
        void OnNetworkInstanceBroadcastingData(Entity instanceEntity);
        void OnNetworkInstancePostBroadcastingData(Entity instanceEntity);
        void OnNetworkInstanceRemoved(Entity instanceEntity);
    }

    public abstract class NetworkComponentSystem : ComponentSystem, INetworkComponentSystem
    {
        protected ReadOnlyDictionary<int, NetworkInstanceData> AliveInstances;

        /// <summary>
        /// Called when an instance got added.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceAdded(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceBroadcastingData(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance has finished broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstancePostBroadcastingData(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is being removed.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceRemoved(Entity instanceEntity)
        {
        }

        internal void InternalSetAliveInstances(Dictionary<int, NetworkInstanceData> origin)
        {
            AliveInstances = new ReadOnlyDictionary<int, NetworkInstanceData>(origin);
        }
    }
    
    public abstract class NetworkJobComponentSystem : JobComponentSystem, INetworkComponentSystem
    {
        protected ReadOnlyDictionary<int, NetworkInstanceData> AliveInstances;
        
        /// <summary>
        /// Called when an instance got added.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceAdded(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceBroadcastingData(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance has finished broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstancePostBroadcastingData(Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is being removed.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceRemoved(Entity instanceEntity)
        {
        }
        
        internal void InternalSetAliveInstances(Dictionary<int, NetworkInstanceData> origin)
        {
            AliveInstances = new ReadOnlyDictionary<int, NetworkInstanceData>(origin);
        }
    }
}
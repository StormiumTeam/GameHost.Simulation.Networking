using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Entities;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public interface INetworkComponentSystem
    {
        void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity);
        void OnNetworkInstanceBroadcastingData(int instanceId, Entity instanceEntity);
        void OnNetworkInstancePostBroadcastingData(int instanceId, Entity instanceEntity);
        void OnNetworkInstanceRemoved(int instanceId, Entity instanceEntity);
        void Implement<TImpl>(TImpl implementable) where TImpl : class, INetworkImplementable;
    }

    public interface INetworkComponentSystemInternal
    {
        void InternalOnNetworkInstanceAdded(int instanceId, Entity instanceEntity);
    }

    public abstract class NetworkComponentSystem : ComponentSystem, INetworkComponentSystem, INetworkComponentSystemInternal
    {
        protected ReadOnlyDictionary<int, NetworkInstanceData> AliveInstances;

        /// <summary>
        /// Called when an instance got added.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceBroadcastingData(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance has finished broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstancePostBroadcastingData(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is being removed.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceRemoved(int instanceId, Entity instanceEntity)
        {
        }

        public void Implement<TImpl>(TImpl implementable) where TImpl : class, INetworkImplementable
        {
            
        }

        internal void InternalSetAliveInstances(Dictionary<int, NetworkInstanceData> origin)
        {
            AliveInstances = new ReadOnlyDictionary<int, NetworkInstanceData>(origin);
        }

        void INetworkComponentSystemInternal.InternalOnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            
        }
    }
    
    public abstract class NetworkJobComponentSystem : JobComponentSystem, INetworkComponentSystem, INetworkComponentSystemInternal
    {
        protected ReadOnlyDictionary<int, NetworkInstanceData> AliveInstances;
        
        /// <summary>
        /// Called when an instance got added.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceBroadcastingData(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance has finished broadcasting vital data right after connection.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstancePostBroadcastingData(int instanceId, Entity instanceEntity)
        {
        }
        
        /// <summary>
        /// Called when an instance is being removed.
        /// </summary>
        /// <param name="instanceEntity"></param>
        public virtual void OnNetworkInstanceRemoved(int instanceId, Entity instanceEntity)
        {
        }

        public void Implement<TImpl>(TImpl implementable) where TImpl : class, INetworkImplementable
        {
            
        }

        internal void InternalSetAliveInstances(Dictionary<int, NetworkInstanceData> origin)
        {
            AliveInstances = new ReadOnlyDictionary<int, NetworkInstanceData>(origin);
        }
        
        void INetworkComponentSystemInternal.InternalOnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            
        }
    }
}
using System.Collections.Generic;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public interface INetworkImplementable
    {
        
    }
    
    public class NetInstanceValueLink<TValue> : INetworkImplementable
    {
        private Dictionary<int, TValue> m_Values;

        public bool IsValid;

        public void Set(int instanceId, TValue value)
        {
            m_Values[instanceId] = value;
        }

        public TValue Get(int instanceId)
        {
            return m_Values[instanceId];
        }

        internal void CreateLink(int instanceId)
        {
            m_Values = new Dictionary<int, TValue>();
        }

        internal void DestroyLink(int instanceId)
        {
            m_Values.Clear();
            m_Values = null;
        }
    }
}
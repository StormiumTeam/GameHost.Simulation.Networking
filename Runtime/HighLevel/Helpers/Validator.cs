using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public static class QueryTypeManager
    {
        private static int                     s_Counter;
        private static Dictionary<int, string> s_QueryNames;
        private static Dictionary<string, int> s_QueryTypes;

        static QueryTypeManager()
        {
            s_Counter    = 1;
            s_QueryNames = new Dictionary<int, string>();
            s_QueryTypes = new Dictionary<string, int>();
        }

        public static int Create(string name)
        {
            if (s_QueryTypes.ContainsKey(name))
            {
                return s_QueryTypes[name];
            }

            s_Counter++;

            s_QueryNames[s_Counter] = name;
            s_QueryTypes[name]      = s_Counter;
            
            Debug.Log($"Created QueryType({s_Counter}) : {name}");

            return s_Counter;
        }

        public static string GetName(int type)
        {
            return s_QueryNames[type];
        }

        public static int GetType(string name)
        {
            return s_QueryTypes[name];
        }
    }

    [Flags]
    public enum QueryStatus
    {
        NoState = 0,
        Waiting = 1 << 0,
        Error   = 1 << 1,
        Valid   = 1 << 2,
        ValidWithErrors = Valid | Error
    }

    public struct QueryBuffer : IBufferElementData
    {
        public int         Type;
        public QueryStatus Status;

        public QueryBuffer(int type, QueryStatus status)
        {
            Type   = type;
            Status = status;
        }
    }

    public struct ValidatorManager
    {
        public EntityManager EntityManager;
        public Entity        Target;

        private NativeValidatorManager m_Native;

        public ValidatorManager(EntityManager entityManager, Entity target)
        {
            m_Native = new NativeValidatorManager(entityManager.GetBuffer<QueryBuffer>(target));

            EntityManager = entityManager;
            Target        = target;
        }

        public void Add(int type, QueryStatus initStatus = QueryStatus.Waiting)
        {
            m_Native.Add(type, initStatus);
        }

        public void Set(int type, QueryStatus status)
        {
            m_Native.Set(type, status);
        }
        
        public bool Has(int type)
        {
            return m_Native.Has(type);
        }
    }

    public struct NativeValidatorManager
    {
        public DynamicBuffer<QueryBuffer> QueryBuffer;

        public NativeValidatorManager(DynamicBuffer<QueryBuffer> queryBuffer)
        {
            QueryBuffer = queryBuffer;
        }

        public void Add(int type, QueryStatus initStatus = QueryStatus.Waiting)
        {
            for (var i = 0; i != QueryBuffer.Length; i++)
            {
                if (QueryBuffer[i].Type == type) return;
            }

            QueryBuffer.Add(new QueryBuffer(type, initStatus));
        }

        public void Set(int type, QueryStatus status)
        {
            for (var i = 0; i != QueryBuffer.Length; i++)
            {
                if (QueryBuffer[i].Type != type) continue;

                QueryBuffer[i] = new QueryBuffer(type, status);
                return;
            }
        }

        public bool Has(int type)
        {
            for (var i = 0; i != QueryBuffer.Length; i++)
            {
                if (QueryBuffer[i].Type == type) return true;
            }

            return false;
        }
    }
}
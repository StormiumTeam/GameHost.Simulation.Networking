using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using UnityEngine;
using PATTERN_ID_TYPE = System.Int32;
using PATTERN_STRING_LINK = System.Collections.Generic.Dictionary<string, package.stormiumteam.networking.PatternIdent>;
using PATTERN_RESULT_LINK = System.Collections.Generic.Dictionary<string, package.stormiumteam.networking.PatternResult>;
using PATTERN_ID_LINK = System.Collections.Generic.Dictionary<int, string>;
// ReSharper disable BuiltInTypeReferenceStyle

namespace package.stormiumteam.networking
{
    public class PatternBank : IDisposable
    {        
        private PATTERN_STRING_LINK m_StringLink;
        private PATTERN_ID_LINK     m_IdLink;
        private PATTERN_RESULT_LINK m_ResultLink;
        private PATTERN_ID_TYPE     m_IdCounter;

        public event Action<PatternResult> PatternRegister;

        public readonly int InstanceId;

        public PatternBank(int instanceId)
        {
            InstanceId   = instanceId;
            m_StringLink = new PATTERN_STRING_LINK();
            m_IdLink     = new PATTERN_ID_LINK();
            m_ResultLink = new PATTERN_RESULT_LINK();

            m_IdCounter = 1;
        }

        public int Count => m_IdLink.Count;

        public PatternResult Register(PatternIdent patternIdent)
        {
            if (InstanceId != 0) throw new InvalidOperationException();
            
            if (!m_IdLink.ContainsValue(patternIdent.Name))
            {
                var id = m_IdCounter++;
                m_IdLink[id] = patternIdent.Name;

                var patternResult = new PatternResult
                {
                    Id            = id,
                    InternalIdent = patternIdent
                };

                m_ResultLink[patternIdent.Name] = patternResult;
                PatternRegister?.Invoke(patternResult);
            }

            m_StringLink[patternIdent.Name] = patternIdent;

            return GetPatternResult(patternIdent);
        }

        public void RegisterObject(object patternHolder)
        {
            PatternObjectRegister.ObjRegister(patternHolder, this);
        }

        public bool HasPattern(PatternIdent patternIdent)
        {
            return m_StringLink.ContainsKey(patternIdent.Name);
        }

        public PatternResult GetPatternResult(PatternIdent pattern)
        {
            return m_ResultLink[pattern.Name];
        }

        public ReadOnlyDictionary<string, PatternResult> GetResults()
        {
            return new ReadOnlyDictionary<string, PatternResult>(m_ResultLink);
        }
        
        public void Dispose()
        {
            m_StringLink.Clear();
            m_IdLink.Clear();
            m_ResultLink.Clear();
        }

        public void ForeignForceLink(PatternResult patternResult)
        {
            if (InstanceId == 0) throw new InvalidOperationException();

            m_IdLink[patternResult.Id] = patternResult.InternalIdent.Name;
            m_ResultLink[patternResult.InternalIdent.Name] = patternResult;
            m_StringLink[patternResult.InternalIdent.Name] = patternResult.InternalIdent;
        }
    }

    internal static class PatternObjectRegister
    {
        internal static void ObjRegister(object holder, PatternBank bank)
        {
            var type = holder.GetType();
            var fields = type.GetFields
            (
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
            );

            foreach (var field in fields)
            {
                if (field.FieldType != typeof(PatternIdent) && field.FieldType != typeof(PatternResult)) continue;

                var nv               = field.GetCustomAttribute<PatternAttribute>();
                var nameAttribute    = field.GetCustomAttribute<PatternNameAttribute>();
                var versionAttribute = field.GetCustomAttribute<PatternVersionAttribute>();

                var pattern = new PatternIdent {Name = nv?.Name ?? nameAttribute?.Value};

                if (string.IsNullOrEmpty(pattern.Name))
                {
                    pattern.Name = $"{type.Namespace}:{type.Name}.{field.Name}";
                }

                if (nv != null) pattern.Version                    = nv.Version;
                else if (versionAttribute != null) pattern.Version = versionAttribute.Value;

                var result = bank.Register(pattern);

                if (field.FieldType == typeof(PatternIdent))
                    field.SetValue(holder, pattern);
                else
                    field.SetValue(holder, result);
            }
        }
    }
}
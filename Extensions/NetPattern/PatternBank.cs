using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using PATTERN_ID_TYPE = System.Int32;
using PATTERN_STRING_LINK = System.Collections.Generic.Dictionary<string, package.stormiumteam.networking.extensions.PatternIdent>;
using PATTERN_RESULT_LINK = System.Collections.Generic.Dictionary<string, package.stormiumteam.networking.extensions.PatternResult>;
using PATTERN_ID_LINK = System.Collections.Generic.Dictionary<int, string>;
// ReSharper disable BuiltInTypeReferenceStyle

namespace package.stormiumteam.networking.extensions
{
    public class PatternBank : IDisposable
    {
        private PATTERN_STRING_LINK m_StringLink;
        private PATTERN_ID_LINK     m_IdLink;
        private PATTERN_RESULT_LINK m_ResultLink;
        private PATTERN_ID_TYPE     m_IdCounter;

        public readonly int InstanceId;

        public PatternBank(int instanceId)
        {
            InstanceId   = instanceId;
            m_StringLink = new PATTERN_STRING_LINK();
            m_IdLink     = new PATTERN_ID_LINK();
            m_ResultLink = new PATTERN_RESULT_LINK();

            m_IdCounter = 1;
        }

        public PatternResult Register(PatternIdent patternIdent)
        {
            PATTERN_ID_TYPE id;
            if (!m_IdLink.ContainsValue(patternIdent.Name))
            {
                id               = m_IdCounter++;
                m_IdLink[id] = patternIdent.Name;
                m_ResultLink[patternIdent.Name] = new PatternResult
                {
                    Id = id,
                    InternalIdent = patternIdent
                };
            }

            m_StringLink[patternIdent.Name] = patternIdent;
            
            Debug.Log($"Pattern added: {patternIdent.Name}");

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

        public void Dispose()
        {
            m_StringLink.Clear();
            m_IdLink.Clear();
            m_ResultLink.Clear();
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
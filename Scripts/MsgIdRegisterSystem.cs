using System;
using System.Collections.Generic;
using System.Reflection;
using OdinSerializer.Utilities;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking
{
    public sealed class PatternNvAttribute : Attribute
    {
        public string Name;
        public byte Version;

        public PatternNvAttribute(string name, byte version)
        {
            Name = name;
            Version = version;
        }
    }
    
    public sealed class PatternNameAttribute : Attribute
    {
        public string Value;

        public PatternNameAttribute(string value)
        {
            Value = value;
        }
    }

    public sealed class PatternVersionAttribute : Attribute
    {
        public byte Value;

        public PatternVersionAttribute(byte value)
        {
            Value = value;
        }
    }
    
    public class MsgIdRegisterSystem : ComponentSystem
    {
        public event Action<int, MessageIdent> OnNewPattern;
        
        public FastDictionary<string, MessageIdent> InstancePatterns
            = new FastDictionary<string, MessageIdent>();

        public FastDictionary<int, MessageIdent> PatternsLink = new FastDictionary<int, MessageIdent>();

        public void Register(object holder)
        {
            var htype = holder.GetType();
            var fields = htype.GetFields
            (
                BindingFlags.Public
                | BindingFlags.NonPublic
                | BindingFlags.Static
                | BindingFlags.Instance
            );
            foreach (var field in fields)
            {
                if (field.FieldType != typeof(MessageIdent)) continue;

                var nv = field.GetAttribute<PatternNvAttribute>();
                var nameAttribute = field.GetAttribute<PatternNameAttribute>();
                var versionAttribute = field.GetAttribute<PatternVersionAttribute>();

                var msgId = (MessageIdent)field.GetValue(holder);
                msgId.Id = nv?.Name ?? nameAttribute?.Value;
                
                if (string.IsNullOrEmpty(msgId.Id))
                {
                    msgId.Id = $"{htype.Namespace}:{htype.Name}.{field.Name}";
                }

                if (nv != null) msgId.Version = nv.Version;
                else if (versionAttribute != null) msgId.Version = versionAttribute.Value;
                
                field.SetValue(holder, msgId);

                Debug.Log($"Register msg$({msgId.Id}, v.{msgId.Version})");
                Register(msgId);
            }
        }

        public MessageIdent Register(MessageIdent ident)
        {
            if (!HasPatternLinked(ident))
            {
                PatternsLink[PatternsLink.Count] = ident;
            }

            var v = InstancePatterns[ident.Id] = ident;

            OnNewPattern?.Invoke(GetLinkFromIdent(ident.Id), v);
            
            return v;
        }

        public MessageIdent Register(string nameId, byte version)
        {
            return Register(new MessageIdent()
            {
                Id      = nameId,
                Version = version
            });
        }

        public bool HasPatternLinked(MessageIdent ident)
        {
            foreach (var link in PatternsLink)
            {
                if (link.Value.Id == ident.Id)
                    return true;
            }

            return false;
        }

        public MessageIdent GetPatternFromLink(int linkId)
        {
            if (!PatternsLink.FastTryGet(linkId, out var value))
                value = MessageIdent.Zero;
            return value;
        }

        public int GetLinkFromIdent(string ident)
        {
            foreach (var link in PatternsLink)
            {
                if (link.Value.Id == ident)
                    return link.Key;
            }

            Debug.LogWarning($"[{nameof(MsgIdRegisterSystem)}] No link found for: {ident}");
            
            return 0;
        }

        protected override void OnCreateManager(int capacity)
        {
            PatternsLink[0] = MessageIdent.Zero;
        }

        protected override void OnUpdate()
        {
        }

        public void ForceLinkRegister(string patternId, byte version, int linkId)
        {
            var ident = new MessageIdent()
            {
                Id      = patternId,
                Version = version
            };
            
            PatternsLink[linkId] = ident;
            InstancePatterns[ident.Id] = ident;
        }
    }
}
using System;
using System.Collections.Generic;
using Unity.Entities;

namespace package.stormiumteam.networking
{
    public class MsgIdRegisterSystem : ComponentSystem
    {
        public event Action<int, MessageIdent> OnNewPattern;
        
        public FastDictionary<string, MessageIdent> InstancePatterns
            = new FastDictionary<string, MessageIdent>();

        public FastDictionary<int, MessageIdent> PatternsLink = new FastDictionary<int, MessageIdent>();

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
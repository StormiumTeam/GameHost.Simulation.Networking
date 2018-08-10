using System;
using System.Collections.Generic;

namespace package.stormiumteam.networking.ecs
{
    public class ConnectionTypeManager : NetworkConnectionSystem
    {
        public FastDictionary<int, Type> TypeMap = new FastDictionary<int, Type>();
        
        protected override void OnUpdate()
        {
            
        }

        public int AddType(Type type)
        {
            var typeIndex = TypeMap.Count;
            TypeMap[typeIndex] = type;
            return typeIndex;
        }

        public Type GetType(int type)
        {
            return TypeMap[type];
        }

        public override void OnInstanceBroadcastingData(NetPeerInstance peerInstance)
        {
            
        }
    }
}
using package.stormiumteam.networking.runtime.highlevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public struct NetworkConnection
    {
        public int ParentId;
        public int Id;

        public NetworkConnection(int id, int parentId = 0)
        {
            Id       = id;
            ParentId = parentId;
        }

        public override string ToString()
        {
            return $"Connection(id:{Id}, pid:{ParentId})";
        }

        public static NetworkConnection New(int parentId = 0)
        {
            return new NetworkConnection(IncrementCounter(), parentId);
        }

        private static int IncrementCounter()
        {
            var counter       = s_NativeCounter;
            var valueToReturn = counter[0];

            counter[0] = valueToReturn + 1;

            return valueToReturn;
        }

        private static NativeArray<int> s_NativeCounter;
        public static  long             Count => s_NativeCounter[0];

        static NetworkConnection()
        {
            Create();
        }

        [BurstDiscard]
        private static void Create()
        {
            s_NativeCounter = new NativeArray<int>(1, Allocator.Persistent) {[0] = 1};

            PlayerLoopManager.RegisterDomainUnload(() => { s_NativeCounter.Dispose(); }, 10002);
        }
    }
}
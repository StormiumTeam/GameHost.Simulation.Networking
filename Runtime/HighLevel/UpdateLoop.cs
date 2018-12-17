using Unity.Entities;
using UnityEngine.Experimental.PlayerLoop;

namespace package.stormiumteam.networking.runtime.highlevel
{
    /// <summary>
    /// 
    /// </summary>
    /// <remarks>
    /// then NetworkManager
    /// then (IntInit)
    /// then NetworkEventManager
    /// then NetowkrConnectionManager
    /// then (IntEnd)
    /// </remarks>
    public struct UpdateLoop
    {
        [UpdateInGroup(typeof(Update))]
        public class IntNetworkManager : BarrierSystem
        {

        }

        [UpdateAfter(typeof(IntNetworkManager))]
        public class IntInit : BarrierSystem
        {

        }

        [UpdateAfter(typeof(IntInit))]
        public class IntNetworkEventManager : BarrierSystem
        {
        }

        [UpdateAfter(typeof(IntNetworkEventManager))]
        public class IntNetworkCreateIncomingInstance : BarrierSystem
        {
        }

        [UpdateAfter(typeof(IntNetworkCreateIncomingInstance))]
        public class IntNetworkConnectionManager : BarrierSystem
        {
        }

        [UpdateAfter(typeof(IntNetworkConnectionManager))]
        public class IntNetworkValidateInstance : BarrierSystem
        {
        }

        [UpdateAfter(typeof(IntNetworkValidateInstance))]
        public class IntEnd : BarrierSystem
        {

        }
    }
}
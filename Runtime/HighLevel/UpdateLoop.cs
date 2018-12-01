using Unity.Entities;
using UnityEngine.Experimental.PlayerLoop;

namespace package.stormiumteam.networking.Runtime.HighLevel
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
        public abstract class IntNetworkManager
        {
            
        }
        
        [UpdateAfter(typeof(IntNetworkManager))]
        public abstract class IntInit
        {
            
        }
        
        [UpdateAfter(typeof(IntInit))]
        public class IntNetworkEventManager
        {
        }
        
        [UpdateAfter(typeof(IntNetworkEventManager))]
        public class IntNetworkConnectionManager
        {
        }
        
        [UpdateAfter(typeof(IntNetworkConnectionManager))]
        public abstract class IntNetworkValidateInstance
        {
        }

        [UpdateAfter(typeof(IntNetworkValidateInstance))]
        public abstract class IntEnd
        {
            
        }
    }
}
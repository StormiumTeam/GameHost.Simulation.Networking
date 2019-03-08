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
    /// then NetworkCreateIncomingInstance
    /// then NetworkValidateInstance
    /// then (IntEnd)
    /// </remarks>
    public class UpdateLoop : ComponentSystemGroup
    {
        [UpdateInGroup(typeof(PresentationSystemGroup))]
        public class IntNetworkManager : ComponentSystemGroup
        {
            protected override void OnCreateManager()
            {
                base.OnCreateManager();
                
                AddSystemToUpdateList(World.GetOrCreateManager<NetworkManager>());
            }
        }

        [UpdateAfter(typeof(IntNetworkManager))]
        public class IntInit : ComponentSystemGroup
        {
        }

        [UpdateAfter(typeof(IntInit))]
        public class IntNetworkEventManager : ComponentSystemGroup
        {
            protected override void OnCreateManager()
            {
                base.OnCreateManager();
                
                AddSystemToUpdateList(World.GetOrCreateManager<NetworkEventManager>());
            }
        }

        [UpdateAfter(typeof(IntNetworkEventManager))]
        public class IntNetworkCreateIncomingInstance : ComponentSystemGroup
        {
            protected override void OnCreateManager()
            {
                base.OnCreateManager();
                
                AddSystemToUpdateList(World.GetOrCreateManager<NetworkCreateIncomingInstanceSystem>());
            }
        }

        [UpdateAfter(typeof(IntNetworkCreateIncomingInstance))]
        public class IntNetworkValidateInstance : ComponentSystemGroup
        {
            protected override void OnCreateManager()
            {
                base.OnCreateManager();
                
                AddSystemToUpdateList(World.GetOrCreateManager<IntNetworkValidateInstance>());
            }
        }

        [UpdateAfter(typeof(IntNetworkValidateInstance))]
        public class IntEnd : ComponentSystemGroup
        {

        }
    }
}
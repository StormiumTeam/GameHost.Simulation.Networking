using Unity.Entities;

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
            protected override void OnCreate()
            {
                base.OnCreate();
                
               // AddSystemToUpdateList(World.GetOrCreateSystem<NetworkManager>());
            }
        }

        [UpdateAfter(typeof(IntNetworkManager))]
        public class IntInit : ComponentSystemGroup
        {
        }

        [UpdateAfter(typeof(IntInit))]
        public class IntNetworkEventManager : ComponentSystemGroup
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                
               // AddSystemToUpdateList(World.GetOrCreateSystem<NetworkEventManager>());
            }
        }

        [UpdateAfter(typeof(IntNetworkEventManager))]
        public class IntNetworkCreateIncomingInstance : ComponentSystemGroup
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                
               // AddSystemToUpdateList(World.GetOrCreateSystem<NetworkCreateIncomingInstanceSystem>());
            }
        }

        [UpdateAfter(typeof(IntNetworkCreateIncomingInstance))]
        public class IntNetworkValidateInstance : ComponentSystemGroup
        {
            protected override void OnCreate()
            {
                base.OnCreate();
                
               // AddSystemToUpdateList(World.GetOrCreateSystem<NetworkValidateInstances>());
            }
        }

        [UpdateAfter(typeof(IntNetworkValidateInstance))]
        public class IntEnd : ComponentSystemGroup
        {

        }
    }
}
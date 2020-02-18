using Unity.Entities;

namespace Unity.NetCode
{
    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    public class ClientInitializationSystemGroup : InitializationSystemGroup
    {
        protected override void OnUpdate()
        {
#pragma warning disable 618
            // we're keeping World.DefaultGameObjectInjectionWorld until we can properly remove them all
            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            World.DefaultGameObjectInjectionWorld = World;
            base.OnUpdate();
            World.DefaultGameObjectInjectionWorld = defaultWorld;
#pragma warning restore 618
        }
    }

#if !UNITY_SERVER
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    [AlwaysUpdateSystem]
    [UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
    public class TickClientInitializationSystem : ComponentSystemGroup
    {
        public override void SortSystemUpdateList()
        {
        }
    }
#endif
}
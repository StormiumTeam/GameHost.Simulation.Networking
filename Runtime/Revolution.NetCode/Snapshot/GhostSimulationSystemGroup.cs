using Revolution;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Transforms;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Networking.Transport.Utilities;

namespace Unity.NetCode
{
    [UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
    [UpdateBefore(typeof(TransformSystemGroup))]
    public class GhostSimulationSystemGroup : ComponentSystemGroup
    {
    }

    [UpdateInGroup(typeof(GhostSimulationSystemGroup))]
    [UpdateBefore(typeof(GhostPredictionSystemGroup))]
    [UpdateInWorld(UpdateInWorld.TargetWorld.Client)]
    public class GhostUpdateSystemGroup : ComponentSystemGroup
    {
	    public JobHandle LastGhostMapWriter;
    }

    [UpdateInGroup(typeof(ClientSimulationSystemGroup))]
    public class GhostSpawnSystemGroup : ComponentSystemGroup
    {
    }
}

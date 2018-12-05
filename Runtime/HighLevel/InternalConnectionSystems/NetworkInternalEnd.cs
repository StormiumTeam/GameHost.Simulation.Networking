using Unity.Entities;
using Unity.Jobs;

namespace package.stormiumteam.networking.runtime.highlevel
{
    public class NetworkInternalEnd : ComponentSystem
    {
        internal JobHandle JobHandle;
        
        protected override void OnUpdate()
        {
            JobHandle.Complete();
        }
    }
}
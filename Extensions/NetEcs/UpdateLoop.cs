using Unity.Entities;

namespace StormiumShared.Core
{
    public static class UpdateLoop
    {
        public class ReadStates : ComponentSystem
        {
            protected override void OnUpdate()
            {

            }
        }
        
        [UpdateAfter(typeof(ReadStates))]
        public class ManageSnapshot : ComponentSystem
        {
            protected override void OnUpdate()
            {

            }
        }
        
        [UpdateAfter(typeof(ManageSnapshot))]
        public class WriteStates : ComponentSystem
        {
            protected override void OnUpdate()
            {

            }
        }
        
        [UpdateAfter(typeof(WriteStates))]
        public class UpdateDataChangeComponents : ComponentSystem
        {
            protected override void OnUpdate()
            {

            }
        }
    }
}
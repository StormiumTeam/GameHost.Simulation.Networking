using Unity.Entities;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public class NetworkCreateIncomingInstanceSystem : NetworkComponentSystem
    {
        [Inject] private NetworkManager m_NetworkManager;
        
        private ComponentGroup m_Group;
        
        protected override void OnCreateManager()
        {
            m_Group = GetComponentGroup(typeof(NetworkInstanceData), typeof(NetworkInstanceSharedData), typeof(EventBuffer));
        }

        protected override void OnUpdate()
        {
            var length = m_Group.CalculateLength();
            var entityArray = m_Group.GetEntityArray();
            var instanceArray = m_Group.GetComponentDataArray<NetworkInstanceData>();
            var eventBufferArray  = m_Group.GetBufferArray<EventBuffer>();

            for (int i = 0; i != length; i++)
            {
                var instanceData = instanceArray[i];
                if (!instanceData.IsLocal())
                    continue;

                var entity = entityArray[i];
                var eventBuffer  = eventBufferArray[i];

                for (int j = 0; j != eventBuffer.Length; j++)
                {
                    var ev = eventBuffer[j];
                    if (ev.Event.Type == NetworkEventType.Connected)
                    {
                        //var newInstanceResult = m_NetworkManager.GetIncomingInstance(entity, ev.Event.Invoker);
                    }
                }
            }
        }
    }
}
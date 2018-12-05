using Unity.Entities;

namespace package.stormiumteam.networking.Runtime.HighLevel
{
    public struct ConnectionEntityManagerData
    {
        public bool HasEntity(Entity serverEntity)
        {
            // ...
            return true;
        }

        public void DestroyEntity(Entity serverEntity)
        {
            // ...
        }
    }
    
    public class NetworkEntityManager : NetworkComponentSystem
    {
        protected NetInstanceValueLink<ConnectionEntityManagerData> ConnectionsEntityMgrData;

        
        
        public override void OnNetworkInstanceAdded(int instanceId, Entity instanceEntity)
        {
            
        }

        protected override void OnUpdate()
        {
            
        }
    }
}
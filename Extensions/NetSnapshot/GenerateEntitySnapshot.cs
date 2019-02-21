using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    public struct ModelIdent : IComponentData
    {
        public int Id;

        public ModelIdent(int id)
        {
            Id = id;
        }
    }

    public struct GenerateEntitySnapshot : IComponentData
    {
        
    }

    public struct EntitySnapshotManualDestroy : IComponentData
    {
        
    }
}
using package.stormiumteam.networking;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace StormiumShared.Core.Networking
{
    public interface IStateData
    {
    }

    public abstract unsafe class SnapshotDataStreamerBase : ComponentSystem, ISnapshotSubscribe, ISnapshotManageForClient
    {
        private PatternResult m_PatternResult;

        protected override void OnCreateManager()
        {
            var className = string.Empty;
            var outerType = GetType().DeclaringType;
            while (outerType != null)
            {
                className += outerType.Name + ".";

                outerType = outerType.DeclaringType;
            }

            className += GetType().Name;

            World.GetOrCreateManager<AppEventSystem>().SubscribeToAll(this);
            m_PatternResult = World.GetOrCreateManager<NetPatternSystem>()
                                   .GetLocalBank()
                                   .Register(new PatternIdent($"auto." + GetType().Namespace + "." + className));
        }

        public PatternResult GetSystemPattern()
        {
            return m_PatternResult;
        }

        public virtual void SubscribeSystem()
        {
        }

        public abstract DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime);

        public abstract void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData);

        protected void GetDataAndEntityLength(StSnapshotRuntime runtime, out DataBufferWriter data, out int entityLength, int desiredDataLength = 0)
        {
            entityLength = runtime.Entities.Length;
            data         = new DataBufferWriter(math.max(desiredDataLength, 1024 + entityLength * 4 * sizeof(Entity)), Allocator.TempJob);
        }

        protected void GetEntityLength(StSnapshotRuntime runtime, out int entityLength)
        {
            entityLength = runtime.Entities.Length;
        }

        protected override void OnUpdate()
        {
        }
    }
}
using System;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Networking.Transport;

/*namespace StormiumTeam.Networking.Internal
{
    public struct ComponentStreamState : IComponentData
    {
        public byte Importance;
    }


    public struct ComponentStreamSnapshotData : ISnapshotData<ComponentStreamSnapshotData>
    {
        public int Importance;

        public uint Tick { get; set; }

        public void PredictDelta(uint tick, ref ComponentStreamSnapshotData baseline1, ref ComponentStreamSnapshotData baseline2)
        {
            throw new NotImplementedException();
        }

        public void Serialize(ref ComponentStreamSnapshotData baseline, DataStreamWriter writer, NetworkCompressionModel compressionModel)
        {
            writer.WritePackedIntDelta(Importance, baseline.Importance, compressionModel);
        }

        public void Deserialize(uint                    tick, ref ComponentStreamSnapshotData baseline, DataStreamReader reader, ref DataStreamReader.Context ctx,
                                NetworkCompressionModel compressionModel)
        {
            Tick       = tick;
            Importance = reader.ReadPackedIntDelta(ref ctx, baseline.Importance, compressionModel);
        }

        public void Interpolate(ref ComponentStreamSnapshotData target, float factor)
        {
            Importance = target.Importance;
        }
    }

    public struct ComponentStreamGhostSerializer : IGhostSerializer<ComponentStreamSnapshotData>
    {
        [NativeDisableContainerSafetyRestriction]
        private ArchetypeChunkComponentType<ComponentStreamState> ghostStreamType;

        public int CalculateImportance(ArchetypeChunk chunk)
        {
            return 200;
        }

        public bool WantsPredictionDelta => false;

        public int SnapshotSize => UnsafeUtility.SizeOf<ComponentStreamSnapshotData>();

        public void BeginSerialize(ComponentSystemBase system)
        {
            ghostStreamType = system.GetArchetypeChunkComponentType<ComponentStreamState>();

            streamType = ComponentType.ReadWrite<ComponentStreamSnapshotData>();
        }

        private ComponentType streamType;

        public bool CanSerialize(EntityArchetype arch)
        {
            var components = arch.GetComponentTypes();
            int matches    = 0;
            for (int i = 0; i < components.Length; ++i)
            {
                if (components[i] == streamType)
                    ++matches;
            }

            return (matches == 1);
        }

        public void CopyToSnapshot(ArchetypeChunk chunk, int ent, uint tick, ref ComponentStreamSnapshotData snapshot)
        {
            var stream = chunk.GetNativeArray(ghostStreamType);

            snapshot.Importance = stream[ent].Importance;
            snapshot.Tick       = tick;
        }
    }
}*/
using System.Collections.Generic;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;
using Unity.Collections;

namespace Revolution.NetCode
{
    [UpdateInGroup(typeof(ServerSimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    public sealed class CommandReceiveSystem : ComponentSystem
    {
        private EntityQuery             m_IncomingDataQuery;
        private CommandCollectionSystem m_CommandCollectionSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IncomingDataQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(CommandTargetComponent), typeof(IncomingCommandDataStreamBufferComponent)}
            });
            m_CommandCollectionSystem = World.GetOrCreateSystem<CommandCollectionSystem>();
        }

        protected override void OnUpdate()
        {
            if (m_IncomingDataQuery.CalculateEntityCount() == 0)
                return;

            foreach (var collection in m_CommandCollectionSystem.SystemProcessors.Values)
                collection.Prepare();

            using (var entityArray = m_IncomingDataQuery.ToEntityArray(Allocator.TempJob))
            using (var targetArray = m_IncomingDataQuery.ToComponentDataArray<CommandTargetComponent>(Allocator.TempJob))
            {
                OnArrayProcess(entityArray, targetArray);
            }
        }

        private unsafe void OnArrayProcess(NativeArray<Entity> entities, NativeArray<CommandTargetComponent> targetArray)
        {
            for (var ent = 0; ent < entities.Length; ent++)
            {
                var target = targetArray[ent];
                if (target.targetEntity == default)
                    continue;

                var buffer = EntityManager.GetBuffer<IncomingCommandDataStreamBufferComponent>(entities[ent]);
                if (buffer.Length <= 0)
                    continue;

                var reader = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) buffer.GetUnsafePtr(), buffer.Length);
                var ctx    = default(DataStreamReader.Context);
                var tick   = reader.ReadUInt(ref ctx);
                while (reader.GetBytesRead(ref ctx) < reader.Length)
                {
                    var type = reader.ReadByte(ref ctx);
                    if (!m_CommandCollectionSystem.SystemProcessors.TryGetValue(type, out var processor))
                        throw new KeyNotFoundException($"No processor with type '{type}' found!");

                    processor.BeginDeserialize(target.targetEntity, type);
                    processor.ProcessReceive(tick, reader, ref ctx);
                }
            }
        }
    }
}
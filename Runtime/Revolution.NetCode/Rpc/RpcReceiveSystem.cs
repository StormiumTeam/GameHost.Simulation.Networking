using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Networking.Transport;
using Unity.Networking.Transport.LowLevel.Unsafe;

namespace Revolution.NetCode
{
    [UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
    [UpdateAfter(typeof(NetworkStreamReceiveSystem))]
    public class RpcReceiveSystem : ComponentSystem
    {
        private EntityQuery         m_IncomingDataQuery;
        private RpcCollectionSystem m_RpcCollectionSystem;

        protected override void OnCreate()
        {
            base.OnCreate();

            m_IncomingDataQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new ComponentType[] {typeof(IncomingRpcDataStreamBufferComponent)}
            });
            m_RpcCollectionSystem = World.GetOrCreateSystem<RpcCollectionSystem>();
        }

        protected override void OnUpdate()
        {
            if (m_IncomingDataQuery.CalculateEntityCount() == 0)
                return;

            foreach (var collection in m_RpcCollectionSystem.SystemProcessors.Values)
                collection.Prepare();

            using (var entityArray = m_IncomingDataQuery.ToEntityArray(Allocator.TempJob))
            {
                OnArrayProcess(entityArray);
            }
        }

        private unsafe void OnArrayProcess(NativeArray<Entity> entities)
        {
            foreach (var entity in entities)
            {
                var buffer = EntityManager.GetBuffer<IncomingRpcDataStreamBufferComponent>(entity);
                if (buffer.Length <= 0)
                    continue;

                var dataArray = buffer.ToNativeArray(Allocator.TempJob);
                buffer.Clear();
                
                var reader    = DataStreamUnsafeUtility.CreateReaderFromExistingData((byte*) dataArray.GetUnsafePtr(), dataArray.Length);
                var ctx       = default(DataStreamReader.Context);
                while (reader.GetBytesRead(ref ctx) < reader.Length)
                {
                    var type = (uint) reader.ReadInt(ref ctx);
                    if (!m_RpcCollectionSystem.SystemProcessors.TryGetValue(type, out var processor))
                        throw new KeyNotFoundException($"No processor with type '{type}' found!");

                    processor.BeginDeserialize(entity, type);
                    processor.ProcessReceive(reader, ref ctx);
                }

                dataArray.Dispose();
            }
        }
    }
}
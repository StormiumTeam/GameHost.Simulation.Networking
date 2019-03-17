using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    static unsafe class AutomaticStreamerBurst
    {
        public struct WriteDataPayload<T>
            where T : struct, IComponentData
        {
            public DataBufferWriter                        Buffer;
            public SnapshotReceiver                        Receiver;
            public SnapshotRuntime                       Runtime;
            public int                                     EntityLength;
            public ComponentDataFromEntity<T>              States;
            public ComponentDataFromEntity<DataChanged<T>> Changes;
        }

        public static class CreateCall<T> where T : struct, IComponentData
        {
            public static CallWriteDataAsBurst WriteData()
            {
                return BurstCompiler.CompileDelegate<CallWriteDataAsBurst>(InternalWriteData);
            }

            private static void InternalWriteData(void* payloadPtr)
            {
                UnsafeUtility.CopyPtrToStructure(payloadPtr, out WriteDataPayload<T> payload);

                ref var entityLength = ref payload.EntityLength;
                ref var buffer       = ref payload.Buffer;
                ref var receiver     = ref payload.Receiver;
                ref var runtime      = ref payload.Runtime;
                ref var states       = ref payload.States;
                ref var changes      = ref payload.Changes;

                for (var i = 0; i != entityLength; i++)
                {
                    var entity = runtime.Entities[i].Source;
                    if (!states.Exists(entity))
                    {
                        buffer.WriteUnmanaged(StreamerSkipReason.NoComponent);
                        continue;
                    }

                    var state  = states[entity];
                    var change = new DataChanged<T> {IsDirty = true};
                    if (changes.Exists(entity))
                        change = changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(receiver, change))
                    {
                        buffer.WriteUnmanaged(StreamerSkipReason.Delta);
                        continue;
                    }

                    buffer.WriteUnmanaged(StreamerSkipReason.NoSkip);
                    buffer.WriteRef(ref state);
                }
            }
        }

        public static void CallWriteData<T>(CallWriteDataAsBurst                    call,
                                            DataBufferWriter                        buffer, SnapshotReceiver receiver, SnapshotRuntime runtime, int entityLength,
                                            ComponentDataFromEntity<T>              states,
                                            ComponentDataFromEntity<DataChanged<T>> changes)
            where T : struct, IComponentData
        {
            var payload = new WriteDataPayload<T>
            {
                Buffer       = buffer,
                Receiver     = receiver,
                Runtime      = runtime,
                EntityLength = entityLength,
                States       = states,
                Changes      = changes
            };

            call(UnsafeUtility.AddressOf(ref payload));
        }

        public delegate void CallWriteDataAsBurst(void* payload);
    }
}
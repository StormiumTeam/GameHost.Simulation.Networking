using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace StormiumShared.Core.Networking
{
    public unsafe class ManualStreamerBurst
    {
        public struct WriteDataForEntityPayload
        {
            public int               Index;
            public Entity            Entity;
            public DataBufferWriter  Data;
            public SnapshotReceiver  Receiver;
            public StSnapshotRuntime Runtime;
        }

        public struct WriteDataPayload<T, Tw>
            where T : struct, IComponentData
            where Tw : struct, IWriteEntityDataPayload
        {
            public DataBufferWriter                           Buffer;
            public SnapshotReceiver                           Receiver;
            public StSnapshotRuntime                          Runtime;
            public int                                        EntityLength;
            public ComponentDataFromEntity<T>                 States;
            public ComponentDataFromEntity<DataChanged<T>>    Changes;
            public FunctionPointer<WriteDataForEntityToBurst> WriteFunction;
            public Tw                                         CustomWritePayload;
        }

        public static class CreateCall<T, Tw, Tr> where T : struct, IComponentData
                                                  where Tw : struct, IWriteEntityDataPayload
                                                  where Tr : struct, IReadEntityDataPayload
        {
            public static CallWriteDataAsBurst WriteData()
            {
                return BurstCompiler.CompileDelegate<CallWriteDataAsBurst>(InternalWriteData);
            }

            private static void InternalWriteData(void* payloadPtr)
            {
                UnsafeUtility.CopyPtrToStructure(payloadPtr, out WriteDataPayload<T, Tw> payload);

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
                        buffer.WriteValue(StreamerSkipReason.NoComponent);
                        continue;
                    }

                    var change = new DataChanged<T> {IsDirty = 1};
                    if (changes.Exists(entity))
                        change = changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(receiver, change))
                    {
                        buffer.WriteValue(StreamerSkipReason.Delta);
                        continue;
                    }

                    buffer.WriteValue(StreamerSkipReason.NoSkip);

                    var wdfePayload = new WriteDataForEntityPayload
                    {
                        Index    = i,
                        Entity   = entity,
                        Data     = buffer,
                        Receiver = receiver,
                        Runtime  = runtime
                    };

                    payload.WriteFunction.Invoke(UnsafeUtility.AddressOf(ref wdfePayload), UnsafeUtility.AddressOf(ref payload.CustomWritePayload));
                }
            }
        }

        public static void CallWriteData<T, Tw>(CallWriteDataAsBurst                       call,
                                                DataBufferWriter                           buffer, SnapshotReceiver receiver, StSnapshotRuntime runtime, int entityLength,
                                                ComponentDataFromEntity<T>                 states,
                                                ComponentDataFromEntity<DataChanged<T>>    changes,
                                                FunctionPointer<WriteDataForEntityToBurst> writeFunction,
                                                Tw                                         customWritePayload)
            where T : struct, IComponentData
            where Tw : struct, IWriteEntityDataPayload
        {
            var payload = new WriteDataPayload<T, Tw>
            {
                Buffer             = buffer,
                Receiver           = receiver,
                Runtime            = runtime,
                EntityLength       = entityLength,
                States             = states,
                Changes            = changes,
                WriteFunction      = writeFunction,
                CustomWritePayload = customWritePayload
            };

            call(UnsafeUtility.AddressOf(ref payload));
        }

        public delegate void CallWriteDataAsBurst(void* payload);

        public delegate void wdfe(void* payload);

        public delegate void WriteDataForEntityToBurst(void* payload, void* custom);
    }
}
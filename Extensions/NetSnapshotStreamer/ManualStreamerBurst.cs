namespace StormiumShared.Core.Networking
{
    /*public unsafe class ManualStreamerBurst
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
            where Tw : struct, IWriteEntityDataPayload<T>
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
                                                  where Tw : struct, IWriteEntityDataPayload<T>
                                                  where Tr : struct, IReadEntityDataPayload<T>
        {
            private static bool s_SameWriteType = typeof(T) == typeof(Tw);
            private static bool s_SameReadType = typeof(T) == typeof(Tr);
            
            public static CallWriteDataAsBurst WriteData()
            {
                return BurstCompiler.CompileDelegate<CallWriteDataAsBurst>(InternalWriteData);
            }

            private static void InternalWriteData(void* payloadPtr)
            {
                const byte ReasonNoComponent = (byte) StreamerSkipReason.NoComponent;
                const byte ReasonDelta = (byte) StreamerSkipReason.Delta;
                const byte ReasonNoSkip = (byte) StreamerSkipReason.NoSkip;
                
                UnsafeUtility.CopyPtrToStructure(payloadPtr, out WriteDataPayload<T, Tw> payload);

                var entityLength = payload.EntityLength;
                var buffer       = payload.Buffer;
                var receiver     = payload.Receiver;
                var runtime      = payload.Runtime;
                var states       = payload.States;
                var changes      = payload.Changes;
                
                var wdfePayload = default(WriteDataForEntityPayload);
                var wdfePayloadAddress = UnsafeUtility.AddressOf(ref wdfePayload);
                var customPayloadAddress = UnsafeUtility.AddressOf(ref payload.CustomWritePayload);
                
                wdfePayload.Data     = buffer;
                wdfePayload.Receiver = receiver;
                wdfePayload.Runtime  = runtime;
                
                var m = payload.WriteFunction.Invoke;

                for (var i = 0; i != entityLength; i++)
                {
                    var entity = runtime.Entities[i].Source;
                    if (!states.Exists(entity))
                    {
                        buffer.WriteByte(ReasonNoComponent);
                        continue;
                    }

                    var change = default(DataChanged<T>);
                    change.IsDirty = true;
                    
                    if (changes.Exists(entity))
                        change = changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(receiver, change))
                    {
                        buffer.WriteByte(ReasonDelta);
                        continue;
                    }

                    buffer.WriteByte(ReasonNoSkip);

                    wdfePayload.Index    = i;
                    wdfePayload.Entity   = entity;

                    m(wdfePayloadAddress, customPayloadAddress);
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
            where Tw : struct, IWriteEntityDataPayload<T>
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
    }*/
}
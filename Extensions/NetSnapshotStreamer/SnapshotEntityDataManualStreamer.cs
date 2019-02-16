using System;
using System.Runtime.InteropServices;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
    public abstract unsafe class SnapshotEntityDataManualStreamer<TState, TWriteEntityPayload, TReadEntityPayload> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData
        where TWriteEntityPayload : struct, IWriteEntityDataPayload<TState>
        where TReadEntityPayload : struct, IReadEntityDataPayload<TState>
    {
        internal static SnapshotEntityDataManualStreamer<TState, TWriteEntityPayload, TReadEntityPayload> m_CurrentStreamer;
        internal static IntPtr                                                                            m_WriteDataForEntityOptimizedPtr;

        internal FunctionPointer<ManualStreamerBurst.wdfe> m_FunctionPointerWriteDataForEntity;

        internal TWriteEntityPayload m_CurrentWritePayload;
        internal TReadEntityPayload  m_CurrentReadPayload;

        protected abstract void UpdatePayloadW(ref TWriteEntityPayload current);
        protected abstract void UpdatePayloadR(ref TReadEntityPayload  current);

        private ManualStreamerBurst.CallWriteDataAsBurst m_WriteDataBurst;
        
        [BurstCompile]
        struct WriteJob : IJob
        {
            public DataBufferWriter                           Buffer;
            public SnapshotReceiver                           Receiver;
            public StSnapshotRuntime                          Runtime;
            public int                                        EntityLength;
            
            [ReadOnly]
            public ComponentDataFromEntity<TState>                 States;
            [ReadOnly]
            public ComponentDataFromEntity<DataChanged<TState>>    Changes;
            
            [NativeDisableUnsafePtrRestriction]
            public TWriteEntityPayload cp;
            
            public void Execute()
            {
                const byte ReasonNoComponent = (byte) StreamerSkipReason.NoComponent;
                const byte ReasonDelta       = (byte) StreamerSkipReason.Delta;
                const byte ReasonNoSkip      = (byte) StreamerSkipReason.NoSkip;
                
                for (var i = 0; i != EntityLength; i++)
                {
                    var entity = Runtime.Entities[i].Source;
                    if (!States.Exists(entity))
                    {
                        Buffer.WriteByte(ReasonNoComponent);
                        continue;
                    }

                    var change = default(DataChanged<TState>);
                    change.IsDirty = 1;
                    
                    if (Changes.Exists(entity))
                        change = Changes[entity];

                    if (SnapshotOutputUtils.ShouldSkip(Receiver, change))
                    {
                        Buffer.WriteByte(ReasonDelta);
                        continue;
                    }

                    Buffer.WriteByte(ReasonNoSkip);
                    cp.Write(i, entity, States, Changes, Buffer, Receiver, Runtime);
                }
            }
        }

        protected override unsafe void OnCreateManager()
        {
            base.OnCreateManager();

            try
            {
                m_WriteDataForEntityOptimizedPtr = Marshal.GetFunctionPointerForDelegate(BurstCompiler.CompileDelegate((ManualStreamerBurst.WriteDataForEntityToBurst) OptimizedWriteDataForEntity));
            }
            catch (Exception e)
            {
                Debug.LogError($"Couldn't burst {typeof(TWriteEntityPayload).FullName}.\n Exception Message:\n{e.Message}");
                m_WriteDataForEntityOptimizedPtr = Marshal.GetFunctionPointerForDelegate((ManualStreamerBurst.WriteDataForEntityToBurst) OptimizedWriteDataForEntity);
            }
            
            m_WriteDataBurst = ManualStreamerBurst.CreateCall<TState, TWriteEntityPayload, TReadEntityPayload>.WriteData();
        }

        private static void OptimizedWriteDataForEntity(void* payloadPtr, void* customPtr)
        {
            UnsafeUtility.CopyPtrToStructure(payloadPtr, out ManualStreamerBurst.WriteDataForEntityPayload payload);
            UnsafeUtility.CopyPtrToStructure(customPtr, out TWriteEntityPayload custom);

            //custom.Write(payload.Index, payload.Entity, payload.Data, payload.Receiver, payload.Runtime);
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime)
        {
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);
            UpdateComponentDataFromEntity();

            m_CurrentStreamer = this;
            UpdatePayloadW(ref m_CurrentWritePayload);

            var writeFunction = new FunctionPointer<ManualStreamerBurst.WriteDataForEntityToBurst>(m_WriteDataForEntityOptimizedPtr);

            Profiler.BeginSample("CallWriteData (Bursted)");
            //ManualStreamerBurst.CallWriteData(m_WriteDataBurst, buffer, receiver, runtime, entityLength, States, Changed, writeFunction, m_CurrentWritePayload);
            new WriteJob
            {
                Buffer       = buffer,
                Changes      = Changed,
                States       = States,
                EntityLength = entityLength,
                Receiver     = receiver,
                Runtime      = runtime,
                cp           = m_CurrentWritePayload
            }.Run();
            Profiler.EndSample();
            
            return buffer;
        }
 
        public override void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();
            UpdatePayloadR(ref m_CurrentReadPayload);

            for (var index = 0; index != length; index++)
            {
                var worldEntity = runtime.GetWorldEntityFromGlobal(index);
                var skip        = sysData.ReadValue<StreamerSkipReason>();

                if (skip != StreamerSkipReason.NoSkip)
                {
                    // If the component don't exist in the snapshot, also remove it from our world.
                    if (skip == StreamerSkipReason.NoComponent
                        && StateExists(worldEntity))
                    {
                        EntityManager.RemoveComponent<TState>(worldEntity);
                        // If for some weird reason, it also have the 'DataChanged<T>' component, removed it
                        if (ChangedStateExists(worldEntity))
                        {
                            EntityManager.RemoveComponent<DataChanged<TState>>(worldEntity);
                        }

                        UpdateComponentDataFromEntity();
                    }

                    continue; // skip
                }

                if (!StateExists(worldEntity))
                {
                    EntityManager.AddComponent(worldEntity, typeof(TState));
                    UpdateComponentDataFromEntity();
                }

                m_CurrentReadPayload.Read(index, worldEntity, States, ref sysData, sender, runtime);
            }
        }
    }

    public abstract class SnapshotEntityDataManualStreamer<TState, TMultiEntityPayload> : SnapshotEntityDataManualStreamer<TState, TMultiEntityPayload, TMultiEntityPayload>
        where TState : struct, IComponentData
        where TMultiEntityPayload : struct, IMultiEntityDataPayload<TState>
    {
        protected abstract void UpdatePayload(ref TMultiEntityPayload current);

        protected override void UpdatePayloadR(ref TMultiEntityPayload current)
        {
            UpdatePayload(ref current);
        }

        protected override void UpdatePayloadW(ref TMultiEntityPayload current)
        {
            UpdatePayload(ref current);
        }
    }
}
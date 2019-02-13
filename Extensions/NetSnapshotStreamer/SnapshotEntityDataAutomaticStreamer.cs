using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormiumShared.Core.Networking
{
public abstract class SnapshotEntityDataAutomaticStreamer<TState> : SnapshotEntityDataStreamerBase<TState>
        where TState : struct, IComponentData
    {
        private AutomaticStreamerBurst.CallWriteDataAsBurst m_WriteDataBurst;

        protected override unsafe void OnCreateManager()
        {
            base.OnCreateManager();

            Debug.Log("Will start compiling...");
            m_WriteDataBurst = AutomaticStreamerBurst.CreateCall<TState>.WriteData();
        }

        public override DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime)
        {
            Profiler.BeginSample("Init Variables");
            GetDataAndEntityLength(runtime, out var buffer, out var entityLength);
            UpdateComponentDataFromEntity();
            Profiler.EndSample();

            AutomaticStreamerBurst.CallWriteData(m_WriteDataBurst, buffer, receiver, runtime, entityLength, States, Changed);

            return buffer;
        }

        public override void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData)
        {
            GetEntityLength(runtime, out var length);
            UpdateComponentDataFromEntity();

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

                var state = sysData.ReadValue<TState>();
                EntityManager.SetComponentData(worldEntity, state);
            }
        }
    }
}
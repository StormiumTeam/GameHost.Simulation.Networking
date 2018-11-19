using System.Collections.Generic;
using package.stormiumteam.shared;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace package.stormiumteam.networking.ecs
{
    public struct NetPosition : IComponentData
    {
        public float DeltaProgress;
        public float3 Target;
        public float3 Predicted;
    }

    public struct NetRotation : IComponentData
    {
        public quaternion Value;
    }

    public struct NetScale : IComponentData
    {
        public float3 Value;
    }

    public struct NetSnapshotPosition : ISharedComponentData
    {
        public struct Frame
        {
            public float  Delta;
            public float3 Value;
        }

        public float       DeltaDuration;
        public List<Frame> DeltaPositions;

        public float3 PredictedPosition;

        public NetSnapshotPosition(List<Frame> deltas)
        {
            PredictedPosition = float3.zero;
            DeltaDuration  = 0f;
            DeltaPositions = deltas;
        }
    }

    public struct NetPositionDeadReckoning : IComponentData
    {
        public float3 ResolvedPosition;
    }

    public struct NetPositionDeadReckoningBuffer : IBufferElementData
    {
        public float Timestamp;
        public float Delta;
        public float3 Position;
        public float3 Velocity;

        public NetPositionDeadReckoningBuffer(float timestamp, Vector3 position, Vector3 velocity)
        {
            Timestamp = timestamp;
            Position = position;
            Velocity = velocity;
            Delta = 0;
        }
    }

    public struct NetPositionInterpolator : IComponentData
    {
        public float CurrentTime;
        public float StartedTime;
        public float3 LatestPosition;
        public float3 StartingPosition;
        public float LatestTimestamp;
        public float OldestTimeReceived;

        public float TimeBetween;
        public float TimeSincePrevious;
        public float PieceStartTime;
    }

    public struct NetPositionInterpolatorBuffer : IBufferElementData
    {
        public float Timestamp;
        public float TimeReceived;
        public float3 Position;

        public NetPositionInterpolatorBuffer(float timestamp, float timeReceived, float3 pos)
        {
            Timestamp = timestamp;
            TimeReceived = timeReceived;
            Position = pos;
        }
    }

    public class NetPositionWrapper : ComponentDataWrapper<NetPosition> {}
    public class NetRotationWrapper : ComponentDataWrapper<NetRotation> {}
    public class NetScaleWrapper : ComponentDataWrapper<NetScale> {}
    
    [RequireComponent(typeof(ReferencableGameObject))]
    public class NetTransformWrapper : MonoBehaviour
    {
        private void OnEnable()
        {
            var goEntity = GetComponent<ReferencableGameObject>().GetOrAddComponent<GameObjectEntity>();
            var e = goEntity.Entity;
            
            e.SetOrAddComponentData(new NetPosition());
            e.SetOrAddComponentData(new NetRotation());
            e.SetOrAddComponentData(new NetScale());
        }
    }
}
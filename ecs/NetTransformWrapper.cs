using package.stormiumteam.shared;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace package.stormiumteam.networking.ecs
{
    public struct NetPosition : IComponentData
    {
        public float3 Value;
    }

    public struct NetRotation : IComponentData
    {
        public quaternion Value;
    }

    public struct NetScale : IComponentData
    {
        public float3 Value;
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
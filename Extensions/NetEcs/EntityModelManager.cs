using System.Collections.Generic;
using package.stormiumteam.networking;
using package.stormiumteam.networking.runtime.lowlevel;
using Unity.Entities;
using UnityEngine;

namespace StormiumShared.Core.Networking
{
    public struct BlockComponentSerialization : IBufferElementData
    {
        public int TypeIdx;
    }
    
    public struct ExcludeFromDataStreamer : IComponentData
    {}
    
    public class EntityModelManager : ComponentSystem
    {
        private struct DValue
        {
            public SpawnEntityDelegate Spawn;
            public DestroyEntityDelegate Destroy;
            public WriteModelArray Write;
            public ReadModelArray Read;

            public ComponentType[] Components;
        }
        
        public delegate Entity SpawnEntityDelegate(Entity origin, StSnapshotRuntime snapshotRuntime);
        public delegate void DestroyEntityDelegate(Entity worldEntity);
        
        public delegate void WriteModelArray(ref DataBufferWriter data, SnapshotReceiver receiver, StSnapshotRuntime runtime);
        public delegate void ReadModelArray(ref DataBufferReader data, SnapshotSender sender, StSnapshotRuntime runtime);
        
        private PatternBank m_PatternBank;
        private readonly Dictionary<int, DValue> m_ModelsData = new Dictionary<int, DValue>();

        protected override void OnStartRunning()
        {
            m_PatternBank = World.GetExistingManager<NetPatternSystem>().GetLocalBank();
            
            if (m_PatternBank == null)
                Debug.LogError("The local bank is invalid.");
        }

        protected override void OnUpdate()
        {
            
        }

        public ModelIdent Register(string name, SpawnEntityDelegate spawn, DestroyEntityDelegate destroy)
        {
            // If someone register and we haven't even started running, we need to do it manually
            if (m_PatternBank == null)
            {
                OnStartRunning();
            }
                
            var pattern = m_PatternBank.Register(new PatternIdent(name));

            m_ModelsData[pattern.Id] = new DValue
            {
                Spawn   = spawn,
                Destroy = destroy
            };

            return new ModelIdent(pattern.Id);
        }

        public ModelIdent RegisterFull(string name, ComponentType[] components, SpawnEntityDelegate spawn, DestroyEntityDelegate destroy, WriteModelArray write, ReadModelArray read)
        {
            // If someone register and we haven't even started running, we need to do it manually
            if (m_PatternBank == null)
            {
                OnStartRunning();
            }

            var pattern = m_PatternBank.Register(new PatternIdent(name));

            m_ModelsData[pattern.Id] = new DValue
            {
                Spawn   = spawn,
                Destroy = destroy,
                Write   = write,
                Read    = read,
                
                Components = components
            };

            return new ModelIdent(pattern.Id);
        }

        public Entity SpawnEntity(int modelId, Entity origin, StSnapshotRuntime snapshotRuntime)
        {
            #if UNITY_EDITOR || DEBUG
            if (!m_ModelsData.ContainsKey(modelId))
            {
                PatternResult pattern = default;
                try
                {
                    pattern = m_PatternBank.GetPatternResult(modelId);
                }
                catch
                {
                    // ignored
                }
                
                if (pattern.Id != modelId)
                    Debug.LogError($"Pattern ({pattern.InternalIdent.Name}) isn't correct ({pattern.Id} != {modelId})");
                
                Debug.LogError($"No Spawn Callbacks found for modelId={modelId} ({pattern.InternalIdent.Name})");
            }    
            #endif

            var modelData = m_ModelsData[modelId];
            var entity = modelData.Spawn(origin, snapshotRuntime);

            EntityManager.SetComponentData(entity, new ModelIdent(modelId));

            if (modelData.Components != null)
            {
                var buffer = EntityManager.GetBuffer<BlockComponentSerialization>(entity);
                foreach (var comp in modelData.Components)
                {
                    buffer.Add(new BlockComponentSerialization{TypeIdx = comp.TypeIndex});
                }
            }
            
            return entity;
        }

        public void DestroyEntity(Entity worldEntity, int modelId)
        {
            var callbackObj = m_ModelsData[modelId].Destroy;
            if (callbackObj == null)
            {
                EntityManager.DestroyEntity(worldEntity);
                return;
            }

            callbackObj.Invoke(worldEntity);
        }
    }
}
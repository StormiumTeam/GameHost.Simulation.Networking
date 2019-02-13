using System.Collections.Generic;
using package.stormiumteam.networking;
using Unity.Entities;
using UnityEngine;

namespace StormiumShared.Core.Networking
{
    public class EntityModelManager : ComponentSystem
    {
        public delegate Entity SpawnEntityDelegate(Entity origin, StSnapshotRuntime snapshotRuntime);
        public delegate void DestroyEntityDelegate(Entity worldEntity);
        
        private PatternBank m_PatternBank;
        private readonly Dictionary<int, SpawnEntityDelegate> m_SpawnCallbacks = new Dictionary<int, SpawnEntityDelegate>();
        private readonly Dictionary<int, DestroyEntityDelegate> m_DestroyCallbacks = new Dictionary<int, DestroyEntityDelegate>();

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

            m_SpawnCallbacks[pattern.Id] = spawn;
            m_DestroyCallbacks[pattern.Id] = destroy;

            return new ModelIdent(pattern.Id);
        }

        public Entity SpawnEntity(int modelId, Entity origin, StSnapshotRuntime snapshotRuntime)
        {
            #if UNITY_EDITOR || DEBUG
            if (!m_SpawnCallbacks.ContainsKey(modelId))
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
            
            var entity = m_SpawnCallbacks[modelId].Invoke(origin, snapshotRuntime);

            EntityManager.SetComponentData(entity, new ModelIdent(modelId));
            
            return entity;
        }

        public void DestroyEntity(Entity worldEntity, int modelId)
        {
            var callbackObj = m_DestroyCallbacks[modelId];
            if (callbackObj == null)
            {
                EntityManager.DestroyEntity(worldEntity);
                return;
            }

            callbackObj.Invoke(worldEntity);
        }
    }
}
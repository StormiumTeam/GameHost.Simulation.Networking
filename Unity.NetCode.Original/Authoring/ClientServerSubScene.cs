using System;
using Unity.Entities;
using UnityEngine;
using Unity.Scenes;

namespace Unity.NetCode
{
    public class ClientServerSubScene : MonoBehaviour
    {
        [Flags]
        public enum ConversionTargetType
        {
            None            = 0x0,
            Client          = 0x1,
            Server          = 0x2,
            ClientAndServer = 0x3
        }

        [SerializeField]
        public ConversionTargetType ConversionTarget = ConversionTargetType.ClientAndServer;

        void OnEnable()
        {
            var subScene         = GetComponent<SubScene>();
            var previouslyActive = World.Active;

#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            if ((ConversionTarget & ConversionTargetType.Server) != 0)
            {
                World.Active           = ClientServerBootstrap.serverWorld;
                subScene.AutoLoadScene = true;
                subScene.UpdateSceneEntities(true);
            }
#endif
#if !UNITY_SERVER
            if ((ConversionTarget & ConversionTargetType.Client) != 0)
            {
                foreach (var world in ClientServerBootstrap.clientWorld)
                {
                    World.Active           = world;
                    subScene.AutoLoadScene = true;
                    subScene.UpdateSceneEntities(true);
                }
            }
#endif

            subScene.AutoLoadScene = false;
            World.Active           = previouslyActive;
        }
    }
}
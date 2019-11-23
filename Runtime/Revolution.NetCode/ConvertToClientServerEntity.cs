using System;
using Unity.Entities;
using UnityEngine;

namespace Revolution.NetCode
{
    public class ConvertToClientServerEntity : ConvertToEntity
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

        [HideInInspector]
        public bool canDestroy = false;

        private void Convert()
        {
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                var system = World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<ConvertToEntitySystem>();
                system.AddToBeConverted(World.DefaultGameObjectInjectionWorld, this);
            }
            else
            {
                UnityEngine.Debug.LogWarning($"{nameof(ConvertToEntity)} failed because there is no {nameof(World.DefaultGameObjectInjectionWorld)}", this);
            }
        }

        void Awake()
        {
#if !UNITY_SERVER
            bool convertToClient = (ClientServerBootstrap.clientWorld != null && ClientServerBootstrap.clientWorld.Length >= 1);
#else
        bool convertToClient = true;
#endif
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            bool convertToServer = ClientServerBootstrap.serverWorld != null;
#else
        bool convertToServer = true;
#endif
            if (!convertToClient || !convertToServer)
            {
                UnityEngine.Debug.LogWarning("ConvertEntity failed because there was no Client and Server Worlds", this);
                return;
            }

            convertToClient &= (ConversionTarget & ConversionTargetType.Client) != 0;
            convertToServer &= (ConversionTarget & ConversionTargetType.Server) != 0;

            // Root ConvertToEntity is responsible for converting the whole hierarchy
            if (transform.parent != null && transform.parent.GetComponentInParent<ConvertToEntity>() != null)
                return;

            var defaultWorld = World.Active;
#if !UNITY_SERVER
            if (convertToClient)
            {
                for (int i = 0; i < ClientServerBootstrap.clientWorld.Length; ++i)
                {
                    World.Active = ClientServerBootstrap.clientWorld[i];
                    Convert();
                }
            }
#endif
#if !UNITY_CLIENT || UNITY_SERVER || UNITY_EDITOR
            if (convertToServer)
            {
                World.Active = ClientServerBootstrap.serverWorld;
                Convert();
            }
#endif

            World.Active = defaultWorld;
        }
    }
}
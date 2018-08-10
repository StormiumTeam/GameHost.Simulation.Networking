using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using package.stormiumteam.shared.modding;
using package.stormiumteam.networking;
using Unity.Entities;

namespace Plugins
{
    public class BootstrapInject : CModBootstrap
    {
        protected override void OnRegister()
        {
            InjectToAllAssemblies();
            AppDomain.CurrentDomain.AssemblyLoad += OnNewAssembly;
        }

        protected override void OnUnregister()
        {

        }

        private void OnNewAssembly(object obj, AssemblyLoadEventArgs args)
        {
            InjectToAssembly(args.LoadedAssembly);
        }

        private void InjectToAllAssemblies()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                InjectToAssembly(assembly);
            }
        }

        private void InjectToAssembly(Assembly assembly)
        {
            var systemTypes = assembly.GetTypes().Where(t => 
                t.IsSubclassOf(typeof(NetworkConnectionSystem)) && 
                !t.IsAbstract && 
                !t.ContainsGenericParameters
                && t.GetCustomAttributes(typeof(DisableAutoCreationAttribute), false).Length == 0);

            foreach (var systemType in systemTypes)
            {
                if (systemType.IsSubclassOf(typeof(NetworkConnectionSystem)))
                {
                    NetworkWorld.AddSystemType(systemType);     
                }
            }
        }
    }
}
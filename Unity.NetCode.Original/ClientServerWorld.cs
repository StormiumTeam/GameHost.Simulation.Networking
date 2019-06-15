using System;
using System.Collections.Generic;
using Unity.NetCode;
using Unity.Entities;
using UnityEngine;

namespace Unity.NetCode
{
    internal struct FixedTimeLoop
    {
        public       float accumulatedTime;
        public const float fixedTimeStep = 1f / 60f;
        public const int   maxTimeSteps  = 4;
        public       int   timeSteps;

        public void BeginUpdate()
        {
            accumulatedTime += Time.deltaTime;
            timeSteps       =  0;
        }

        public bool ShouldUpdate()
        {
            if (accumulatedTime < fixedTimeStep)
                return false;
            ++timeSteps;
            if (timeSteps > maxTimeSteps)
            {
                accumulatedTime = accumulatedTime % fixedTimeStep;
                return false;
            }

            accumulatedTime -= fixedTimeStep;
            return true;
        }
    }

    // Update loop for client and server worlds
    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    public class ServerSimulationSystemGroup : ComponentSystemGroup
    {
        private BeginSimulationEntityCommandBufferSystem m_beginBarrier;
        private EndSimulationEntityCommandBufferSystem   m_endBarrier;
        private uint                                     m_ServerTick;
        public  uint                                     ServerTick => m_ServerTick;
        private FixedTimeLoop                            m_fixedTimeLoop;
        public  float                                    UpdateTime      => Time.time - m_fixedTimeLoop.accumulatedTime;
        public  float                                    UpdateDeltaTime => FixedTimeLoop.fixedTimeStep;

        protected override void OnCreateManager()
        {
            m_beginBarrier = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_endBarrier   = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
            m_ServerTick   = 1;
        }

        protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

        public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

        protected override void OnUpdate()
        {
            m_fixedTimeLoop.BeginUpdate();
            while (m_fixedTimeLoop.ShouldUpdate())
            {
                m_beginBarrier.Update();
                base.OnUpdate();
                m_endBarrier.Update();
                ++m_ServerTick;
                if (m_ServerTick == 0)
                    ++m_ServerTick;
            }
        }

        public override void SortSystemUpdateList()
        {
            base.SortSystemUpdateList();
            m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
            m_systemsInGroup.Add(m_beginBarrier);
            m_systemsInGroup.AddRange(m_systemsToUpdate);
            m_systemsInGroup.Add(m_endBarrier);
        }
    }

    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    public class ClientSimulationSystemGroup : ComponentSystemGroup
    {
        private BeginSimulationEntityCommandBufferSystem m_beginBarrier;
        private EndSimulationEntityCommandBufferSystem   m_endBarrier;
        private GhostSpawnSystemGroup                    m_ghostSpawnGroup;
#if UNITY_EDITOR
        public int ClientWorldIndex { get; internal set; }
#endif
        private FixedTimeLoop m_fixedTimeLoop;
        public  float         UpdateTime      => Time.time;
        public  float         UpdateDeltaTime => Time.deltaTime;

        protected override void OnCreateManager()
        {
            m_beginBarrier    = World.GetOrCreateSystem<BeginSimulationEntityCommandBufferSystem>();
            m_endBarrier      = World.GetOrCreateSystem<EndSimulationEntityCommandBufferSystem>();
            m_ghostSpawnGroup = World.GetOrCreateSystem<GhostSpawnSystemGroup>();
        }

        protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

        public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

        protected override void OnUpdate()
        {
            /*m_fixedTimeLoop.BeginUpdate();
            while (m_fixedTimeLoop.ShouldUpdate())
            {
                m_beginBarrier.Update();
                m_ghostSpawnGroup.Update();
                base.OnUpdate();
                m_endBarrier.Update();
            }*/

            m_beginBarrier.Update();
            m_ghostSpawnGroup.Update();
            base.OnUpdate();
            m_endBarrier.Update();
        }

        public override void SortSystemUpdateList()
        {
            base.SortSystemUpdateList();
            m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
            m_systemsInGroup.Add(m_beginBarrier);
            m_systemsInGroup.Add(m_ghostSpawnGroup);
            m_systemsInGroup.AddRange(m_systemsToUpdate);
            m_systemsInGroup.Add(m_endBarrier);
        }
    }

    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    public class ClientPresentationSystemGroup : ComponentSystemGroup
    {
        private BeginPresentationEntityCommandBufferSystem m_beginBarrier;
        private EndPresentationEntityCommandBufferSystem   m_endBarrier;

        protected override void OnCreateManager()
        {
            m_beginBarrier = World.GetOrCreateSystem<BeginPresentationEntityCommandBufferSystem>();
            m_endBarrier   = World.GetOrCreateSystem<EndPresentationEntityCommandBufferSystem>();
        }

        protected List<ComponentSystemBase> m_systemsInGroup = new List<ComponentSystemBase>();

        public override IEnumerable<ComponentSystemBase> Systems => m_systemsInGroup;

        protected override void OnUpdate()
        {
            m_beginBarrier.Update();
            base.OnUpdate();
            m_endBarrier.Update();
        }

        public override void SortSystemUpdateList()
        {
            base.SortSystemUpdateList();
            m_systemsInGroup = new List<ComponentSystemBase>(1 + m_systemsToUpdate.Count + 1);
            m_systemsInGroup.Add(m_beginBarrier);
            m_systemsInGroup.AddRange(m_systemsToUpdate);
            m_systemsInGroup.Add(m_endBarrier);
        }
    }

    [DisableAutoCreation]
    [AlwaysUpdateSystem]
    public class ClientAndServerSimulationSystemGroup : ComponentSystemGroup
    {
    }

    // Ticking of client and server worlds from the main world
#if !UNITY_CLIENT
    [AlwaysUpdateSystem]
    public class TickServerSimulationSystem : ComponentSystemGroup
    {
        public override void SortSystemUpdateList()
        {
        }
    }
#endif
#if !UNITY_SERVER
#if !UNITY_CLIENT
    [UpdateAfter(typeof(TickServerSimulationSystem))]
#endif
    [AlwaysUpdateSystem]
    public class TickClientSimulationSystem : ComponentSystemGroup
    {
        public override void SortSystemUpdateList()
        {
        }
    }

    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [AlwaysUpdateSystem]
    public class TickClientPresentationSystem : ComponentSystemGroup
    {
        public override void SortSystemUpdateList()
        {
        }
    }
#endif

    // Bootstrap of client and server worlds
    public class ClientServerBootstrap : ICustomBootstrap
    {
        public static int        NumClientWorlds = 0;
        public static int        PlayModeType    = 3;
        public static List<Type> Systems;

        public static void CreateClientWorlds()
        {
            if (NumClientWorlds < 0)
                throw new InvalidOperationException();

            if (PlayModeType != 2)
                throw new InvalidOperationException();

#if !UNITY_SERVER
            clientWorld = null;
            ClientSimulationSystemGroup[]   clientSimulationSystemGroup   = null;
            ClientPresentationSystemGroup[] clientPresentationSystemGroup = null;

            clientWorld                   = new World[NumClientWorlds];
            clientSimulationSystemGroup   = new ClientSimulationSystemGroup[clientWorld.Length];
            clientPresentationSystemGroup = new ClientPresentationSystemGroup[clientWorld.Length];
            for (int i = 0; i < clientWorld.Length; ++i)
            {
                clientWorld[i]                 = new World("ClientWorld" + i);
                clientSimulationSystemGroup[i] = clientWorld[i].GetOrCreateSystem<ClientSimulationSystemGroup>();
#if UNITY_EDITOR
                clientSimulationSystemGroup[i].ClientWorldIndex = i;
#endif
                clientPresentationSystemGroup[i] = clientWorld[i].GetOrCreateSystem<ClientPresentationSystemGroup>();
            }
#endif

            foreach (var type in Systems)
            {
                var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
                if (groups.Length == 0)
                    continue;

                foreach (var grp in groups)
                {
                    var group = grp as UpdateInGroupAttribute;
                    if (group.GroupType == typeof(ClientAndServerSimulationSystemGroup))
                    {
                        for (int i = 0; i < clientSimulationSystemGroup.Length; ++i)
                            clientSimulationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateSystem(type) as ComponentSystemBase);
                    }
                    else if (group.GroupType == typeof(ClientSimulationSystemGroup))
                    {
                        for (int i = 0; i < clientSimulationSystemGroup.Length; ++i)
                            clientSimulationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateSystem(type) as ComponentSystemBase);
                    }
                    else if (group.GroupType == typeof(ClientPresentationSystemGroup))
                    {
                        for (int i = 0; i < clientPresentationSystemGroup.Length; ++i)
                            clientPresentationSystemGroup[i]
                                .AddSystemToUpdateList(clientWorld[i].GetOrCreateSystem(type) as ComponentSystemBase);
                    }
                    else
                    {
                        var mask = GetTopLevelWorldMask(group.GroupType);
                        if ((mask & WorldType.ClientWorld) != 0 && clientWorld != null)
                        {
                            for (int i = 0; i < clientWorld.Length; ++i)
                            {
                                var groupSys = clientWorld[i].GetOrCreateSystem(group.GroupType) as ComponentSystemGroup;
                                groupSys.AddSystemToUpdateList(clientWorld[i].GetOrCreateSystem(type) as ComponentSystemBase);
                            }
                        }
                    }
                }
            }

            for (int i = 0; i < clientWorld.Length; ++i)
            {
                clientSimulationSystemGroup[i].SortSystemUpdateList();
                clientPresentationSystemGroup[i].SortSystemUpdateList();
                World.Active.GetOrCreateSystem<TickClientSimulationSystem>().AddSystemToUpdateList(clientSimulationSystemGroup[i]);
                World.Active.GetOrCreateSystem<TickClientPresentationSystem>().AddSystemToUpdateList(clientPresentationSystemGroup[i]);
            }
        }

        public static void CreateServerWorld()
        {
            if (PlayModeType != 1)
                throw new InvalidOperationException();

            if (serverWorld != null)
                throw new InvalidOperationException("Server world already exist...");

            if (Systems == null)
                throw new InvalidOperationException();

#if !UNITY_CLIENT
            serverWorld = null;
            ServerSimulationSystemGroup serverSimulationSystemGroup = null;

            serverWorld                 = new World("ServerWorld");
            serverSimulationSystemGroup = serverWorld.GetOrCreateSystem<ServerSimulationSystemGroup>();
#endif

            foreach (var type in Systems)
            {
                var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
                if (groups.Length == 0)
                    continue;

                foreach (var grp in groups)
                {
                    var group = grp as UpdateInGroupAttribute;
                    if (group.GroupType == typeof(ClientAndServerSimulationSystemGroup))
                    {
                        serverSimulationSystemGroup.AddSystemToUpdateList(serverWorld.GetOrCreateSystem(type) as ComponentSystemBase);
                    }
                    else if (group.GroupType == typeof(ServerSimulationSystemGroup))
                    {
                        serverSimulationSystemGroup.AddSystemToUpdateList(serverWorld.GetOrCreateSystem(type) as ComponentSystemBase);
                    }
                    else
                    {
                        var mask = GetTopLevelWorldMask(group.GroupType);
                        if ((mask & WorldType.ServerWorld) != 0 && serverWorld != null)
                        {
                            var groupSys = serverWorld.GetOrCreateSystem(group.GroupType) as ComponentSystemGroup;
                            groupSys.AddSystemToUpdateList(serverWorld.GetOrCreateSystem(type) as ComponentSystemBase);
                        }
                    }
                }
            }

            serverSimulationSystemGroup.SortSystemUpdateList();
            World.Active.GetOrCreateSystem<TickServerSimulationSystem>().AddSystemToUpdateList(serverSimulationSystemGroup);
        }

        public static void StopClientWorlds(World worldParent)
        {
            if (clientWorld != null)
            {
                foreach (var world in clientWorld)
                {
                    var tickClientPresentationGroup = worldParent.GetExistingSystem<TickClientPresentationSystem>();
                    var tickClientSimulationGroup   = worldParent.GetExistingSystem<TickClientSimulationSystem>();

                    tickClientPresentationGroup.RemoveSystemFromUpdateList(world.GetExistingSystem<ClientPresentationSystemGroup>());
                    tickClientSimulationGroup.RemoveSystemFromUpdateList(world.GetExistingSystem<ClientSimulationSystemGroup>());

                    world.Dispose();
                }
            }

            clientWorld = null;
        }

        public static void StopServerWorld(World worldParent)
        {
            if (serverWorld == null)
                return;

            var tickServerSimulationGroup = worldParent.GetExistingSystem<TickServerSimulationSystem>();
            var simulationGroup           = serverWorld.GetExistingSystem<ServerSimulationSystemGroup>();

            tickServerSimulationGroup.RemoveSystemFromUpdateList(simulationGroup);

            serverWorld.Dispose();
            serverWorld = null;
        }

        public List<Type> Initialize(List<Type> systems)
        {
            // Workaround for initialization being called multiple times when using game object conversion
#if !UNITY_SERVER
            if (clientWorld != null)
                return systems;
#endif
#if !UNITY_CLIENT
            if (serverWorld != null)
                return systems;
#endif

#if !UNITY_SERVER
#if UNITY_EDITOR
            NumClientWorlds = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_NumClients");
            if (NumClientWorlds < 1)
                NumClientWorlds = 1;
            if (NumClientWorlds > 8)
                NumClientWorlds = 8;
            PlayModeType = UnityEditor.EditorPrefs.GetInt("MultiplayerPlayMode_" + UnityEngine.Application.productName + "_Type");
#else
        NumClientWorlds = 1;
#endif
#endif

            var defaultBootstrap = new List<Type>();
            foreach (var type in systems)
            {
                var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
                if (groups.Length == 0)
                {
                    defaultBootstrap.Add(type);
                }

                foreach (var grp in groups)
                {
                    var group = grp as UpdateInGroupAttribute;
                    if (group.GroupType == typeof(ClientAndServerSimulationSystemGroup))
                    {
                    }
                    else if (group.GroupType == typeof(ServerSimulationSystemGroup))
                    {
                    }
                    else if (group.GroupType == typeof(ClientSimulationSystemGroup))
                    {
                    }
                    else if (group.GroupType == typeof(ClientPresentationSystemGroup))
                    {
                    }
                    else
                    {
                        var mask = GetTopLevelWorldMask(group.GroupType);
                        if ((mask & WorldType.DefaultWorld) != 0)
                            defaultBootstrap.Add(type);
                    }
                }
            }

            Systems = systems;

            return defaultBootstrap;
        }

        [Flags]
        enum WorldType
        {
            NoWorld      = 0,
            DefaultWorld = 1,
            ClientWorld  = 2,
            ServerWorld  = 4
        }

        static WorldType GetTopLevelWorldMask(Type type)
        {
            var groups = type.GetCustomAttributes(typeof(UpdateInGroupAttribute), true);
            if (groups.Length == 0)
            {
                if (type == typeof(ClientAndServerSimulationSystemGroup))
                    return WorldType.ClientWorld | WorldType.ServerWorld;
                if (type == typeof(ServerSimulationSystemGroup))
                    return WorldType.ServerWorld;
                if (type == typeof(ClientSimulationSystemGroup) ||
                    type == typeof(ClientPresentationSystemGroup))
                    return WorldType.ClientWorld;
                return WorldType.DefaultWorld;
            }

            WorldType mask = WorldType.NoWorld;
            foreach (var grp in groups)
            {
                var group = grp as UpdateInGroupAttribute;
                mask |= GetTopLevelWorldMask(group.GroupType);
            }

            return mask;
        }

#if !UNITY_SERVER
        public static World[] clientWorld;
#endif
#if !UNITY_CLIENT
        public static World serverWorld;
#endif
    }
}
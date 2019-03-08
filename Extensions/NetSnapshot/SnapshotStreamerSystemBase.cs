using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Text;
using package.stormiumteam.networking;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace StormiumShared.Core.Networking
{
    public interface IStateData
    {
    }

    public abstract unsafe class SnapshotDataStreamerBase : JobComponentSystem, ISnapshotSubscribe, ISnapshotManageForClient
    {
        private PatternResult m_PatternResult;

        private static string GetTypeName(Type type)
        {
            var sb   = new StringBuilder();
            var name = type.Name;
            if (!type.IsGenericType)
                return name;

            sb.Append(name.Substring(0, name.IndexOf('`')));
            sb.Append("<");
            sb.Append(string.Join(", ", type.GetGenericArguments().Select(GetTypeName)));
            sb.Append(">");
            return sb.ToString();
        }

        protected override void OnCreateManager()
        {
            World.GetOrCreateManager<AppEventSystem>().SubscribeToAll(this);
            m_PatternResult = World.GetOrCreateManager<NetPatternSystem>()
                                   .GetLocalBank()
                                   .Register(new PatternIdent($"auto." + GetTypeName(GetType())));
        }

        public PatternResult GetSystemPattern()
        {
            return m_PatternResult;
        }

        public virtual void SubscribeSystem()
        {
        }

        public abstract DataBufferWriter WriteData(SnapshotReceiver receiver, StSnapshotRuntime runtime);

        public abstract void ReadData(SnapshotSender sender, StSnapshotRuntime runtime, DataBufferReader sysData);

        protected void GetDataAndEntityLength(StSnapshotRuntime runtime, out DataBufferWriter data, out int entityLength, int desiredDataLength = 0)
        {
            entityLength = runtime.Entities.Length;
            data         = new DataBufferWriter(math.max(desiredDataLength, 1024 + entityLength * 4 * sizeof(Entity)), Allocator.TempJob);
        }

        protected void GetEntityLength(StSnapshotRuntime runtime, out int entityLength)
        {
            entityLength = runtime.Entities.Length;
        }

        protected override JobHandle OnUpdate(JobHandle job)
        {
            return job;
        }
    }
}
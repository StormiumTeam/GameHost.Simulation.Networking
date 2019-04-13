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
            var codeDomProvider         = CodeDomProvider.CreateProvider("C#");
            var typeReferenceExpression = new CodeTypeReferenceExpression(new CodeTypeReference(type));
            using (var writer = new StringWriter())
            {
                codeDomProvider.GenerateCodeFromExpression(typeReferenceExpression, writer, new CodeGeneratorOptions());
                return writer.GetStringBuilder().ToString();
            } 
        }

        protected override void OnCreate()
        {
            World.GetOrCreateSystem<AppEventSystem>().SubscribeToAll(this);
            m_PatternResult = World.GetOrCreateSystem<NetPatternSystem>()
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

        DataBufferWriter ISnapshotManageForClient.WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime)
        {
            var buffer = new DataBufferWriter(sizeof(int), Allocator.TempJob);
            var lengthMarker = buffer.WriteInt(0);
            buffer.WriteBuffer(WriteData(receiver, runtime));
            buffer.WriteInt(buffer.Length, lengthMarker);
            return buffer;
        }

        void ISnapshotManageForClient.ReadData(SnapshotSender sender, SnapshotRuntime runtime, DataBufferReader dataBufferReader)
        {
            var length = dataBufferReader.ReadValue<int>();
            ReadData(sender, runtime, ref dataBufferReader);
            if (length != dataBufferReader.CurrReadIndex)
            {
                Debug.LogError($"{GetSystemPattern().InternalIdent.Name}.ReadData() -> Error! -> length({length}) != dataBufferReader.CurrReadIndex({dataBufferReader.CurrReadIndex})");
            }
        }

        public abstract DataBufferWriter WriteData(SnapshotReceiver receiver, SnapshotRuntime runtime);

        public abstract void ReadData(SnapshotSender sender, SnapshotRuntime runtime, ref DataBufferReader dataBufferReader);

        protected void GetDataAndEntityLength(SnapshotRuntime runtime, out DataBufferWriter data, out int entityLength, int desiredDataLength = 0)
        {
            entityLength = runtime.Entities.Length;
            data         = new DataBufferWriter(math.max(desiredDataLength, 1024 + entityLength * 4 * sizeof(Entity)), Allocator.TempJob);
        }

        protected void GetEntityLength(SnapshotRuntime runtime, out int entityLength)
        {
            entityLength = runtime.Entities.Length;
        }

        protected override JobHandle OnUpdate(JobHandle job)
        {
            return job;
        }
    }
}
using System;
using System.Collections.Generic;
using Revolution;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Unity.NetCode
{
	[UpdateInGroup(typeof(ClientAndServerInitializationSystemGroup))]
	public class CommandCollectionSystem : ComponentSystem
	{
		public Dictionary<uint, CommandProcessSystemBase> SystemProcessors;

		protected override void OnCreate()
		{
			base.OnCreate();

			SystemProcessors = new Dictionary<uint, CommandProcessSystemBase>();
		}

		protected override void OnUpdate()
		{

		}

		public void SetFixedCollection(Action<World, CollectionBuilder<CommandProcessSystemBase>> build)
		{
			var cb = new CollectionBuilder<CommandProcessSystemBase>();
			build(World, cb);
			SystemProcessors = cb.Build();
		}
	}

	public class CommandProgressSystemGroup : ComponentSystemGroup
	{
		protected override void OnUpdate()
		{
		}
	}

	public abstract class CommandProcessSystemBase : ComponentSystem
	{
		public int    HistorySize = 32;
		public Entity CommandTarget   { get; private set; }
		public uint   SystemCommandId { get; private set; }

		public abstract void Prepare();

		protected override void OnUpdate()
		{
		}

		public void BeginSerialize(Entity target, uint systemId)
		{
			CommandTarget   = target;
			SystemCommandId = SystemCommandId;
		}

		public void BeginDeserialize(Entity target, uint systemId)
		{
			CommandTarget   = target;
			SystemCommandId = systemId;
		}

		public abstract void ProcessReceive(uint tick,       DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel);
		public abstract void ProcessSend(uint    targetTick, DataStreamWriter writer, NetworkCompressionModel      compressionModel);
	}

	public abstract class CommandProcessSystemBase<TCommand> : CommandProcessSystemBase
		where TCommand : struct, ICommandData<TCommand>
	{
		public const uint k_InputBufferSendSize = 4;

		private EntityQuery m_WithoutCommandQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_WithoutCommandQuery = GetEntityQuery(new EntityQueryDesc
			{
				All  = new ComponentType[] {typeof(IncomingCommandDataStreamBufferComponent)},
				None = new ComponentType[] {typeof(TCommand)}
			});
		}

		public override void Prepare()
		{
			using (var entities = m_WithoutCommandQuery.ToEntityArray(Allocator.TempJob))
			{
				foreach (var entity in entities)
				{
					if (EntityManager.HasComponent<TCommand>(entity))
						continue;

					var buffer = EntityManager.AddBuffer<TCommand>(entity);

					buffer.ResizeUninitialized(HistorySize);
					buffer.Clear();
				}
			}
		}

		public override void ProcessReceive(uint tick, DataStreamReader reader, ref DataStreamReader.Context ctx, NetworkCompressionModel compressionModel)
		{
			var buffer = EntityManager.GetBuffer<TCommand>(CommandTarget);

			var baselineReceivedCommand = default(TCommand);
			baselineReceivedCommand.Tick = tick;
			baselineReceivedCommand.ReadFrom(reader, ref ctx, compressionModel);
			// Store received commands in the network command buffer
			buffer.AddCommandData(baselineReceivedCommand);
			for (uint i = 1; i < k_InputBufferSendSize; ++i)
			{
				var receivedCommand = default(TCommand);
				receivedCommand.Tick = tick;
				receivedCommand.ReadFrom(reader, ref ctx, baselineReceivedCommand,
					compressionModel);
				// Store received commands in the network command buffer
				buffer.AddCommandData(receivedCommand);
			}
		}

		public override void ProcessSend(uint targetTick, DataStreamWriter writer, NetworkCompressionModel compressionModel)
		{
			var buffer = EntityManager.GetBuffer<TCommand>(CommandTarget);

			buffer.GetDataAtTick(targetTick, out var baselineInputData);
			baselineInputData.WriteTo(writer, compressionModel);
			for (uint inputIndex = 1; inputIndex < k_InputBufferSendSize; ++inputIndex)
			{
				buffer.GetDataAtTick(targetTick - inputIndex, out var inputData);
				inputData.WriteTo(writer, baselineInputData, compressionModel);
			}
		}
	}

	public class DefaultCommandProcessSystem<TCommand> : CommandProcessSystemBase<TCommand>
		where TCommand : struct, ICommandData<TCommand>
	{
	}
}
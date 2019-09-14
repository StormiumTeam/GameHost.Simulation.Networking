using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution.NetCode
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
		public Entity CommandTarget { get; private set; }
		public uint SystemCommandId { get; private set; }

		public abstract void Prepare();

		protected override void OnUpdate()
		{
		}

		public void BeginSerialize(Entity target, uint systemId)
		{
			CommandTarget = target;
			SystemCommandId = SystemCommandId;
		}

		public void BeginDeserialize(Entity target, uint systemId)
		{
			CommandTarget = target;
			SystemCommandId = systemId;
		}

		public abstract void ProcessReceive(uint tick, DataStreamReader reader, ref DataStreamReader.Context ctx);
		public abstract void ProcessSend(uint targetTick, DataStreamWriter writer);
	}

	public abstract class CommandProcessSystemBase<TCommand> : CommandProcessSystemBase
		where TCommand : struct, ICommandData<TCommand>
	{
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

		public override void ProcessReceive(uint tick, DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var data = default(TCommand);
			data.ReadFrom(reader, ref ctx);

			var buffer = EntityManager.GetBuffer<TCommand>(CommandTarget);
			buffer.AddCommandData(data);
		}

		public override void ProcessSend(uint targetTick, DataStreamWriter writer)
		{
			var buffer = EntityManager.GetBuffer<TCommand>(CommandTarget);
			if (buffer.GetDataAtTick(targetTick, out var commandData) && commandData.Tick == targetTick)
			{
				writer.Write((byte) SystemCommandId);
				commandData.WriteTo(writer);
			}
		}
	}

	public class DefaultCommandProcessSystem<TCommand> : CommandProcessSystemBase<TCommand>
		where TCommand : struct, ICommandData<TCommand>
	{}
}
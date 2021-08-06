using System;
using System.Buffers;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.NetCode.Next.Data
{
	public partial class SimpleSnapshotManager
	{
		static class Reader
		{
			public static void archetypes_data(BitBuffer buffer, SnapshotFrame frame)
			{
				var pool = ArrayPool<uint>.Shared;

				var previousArchetypeId = 0u;
				var previousSystemId    = 0u;

				var archetypeLength = buffer.ReadUIntD4();
				for (var i = 0; i < archetypeLength; i++)
				{
					previousSystemId    = 0;
					previousArchetypeId = buffer.ReadUIntD4Delta(previousArchetypeId);

					var systemLength = buffer.ReadUIntD4();
					var systemArray  = pool.Rent((int)systemLength);
					for (var sys = 0; sys < systemLength; sys++)
					{
						systemArray[sys] = previousSystemId = buffer.ReadUIntD4Delta(previousSystemId);
					}

					pool.Return(systemArray);

					frame.Root.ReplaceArchetype(previousArchetypeId, systemArray.AsSpan(0, (int)systemLength));
				}
			}

			public static void entities(BitBuffer buffer, SnapshotFrame frame)
			{
				var writer = frame.GetWriter();

				GameEntity prev          = default;
				uint       prevArchetype = default;

				var entityCount = buffer.ReadUIntD4();
				for (var i = 0; i < entityCount; i++)
				{
					prev          = new(buffer.ReadUIntD4Delta(prev.Id), buffer.ReadUIntD4Delta(prev.Version));
					prevArchetype = buffer.ReadUIntD4Delta(prevArchetype);
					writer.AddEntity(prev, archetype: prevArchetype);
				}
			}

			[ThreadStatic]
			private static BitBuffer? systemBuffer;

			public static void systems(BitBuffer buffer, SnapshotFrame frame)
			{
				var writer = frame.GetWriter();
				if (!writer.HasBuilt)
					throw new InvalidOperationException("systems() can only be called if the frame has been built");

				foreach (var (_, systemEntity) in frame.SystemStorage)
				{
					systemEntity.Get<ISnapshotSystem>().PrepareRead(writer, systemEntity);
				}

				systemBuffer ??= new();

				uint previousHandle = default;
				while (!buffer.IsFinished)
				{
					previousHandle = buffer.ReadUIntD4Delta(previousHandle);

					systemBuffer.Clear();
					buffer.ReadToExistingBuffer(systemBuffer);

					if (!frame.SystemStorage.TryGetValue(previousHandle, out var systemEntity))
						throw new InvalidOperationException($"System Id '{previousHandle}' not found");

					var system = systemEntity.Get<ISnapshotSystem>();
					system.ReadBuffer(buffer, systemEntity, null);
				}

				foreach (var (_, systemEntity) in frame.SystemStorage)
				{
					systemEntity.Get<ISnapshotSystem>().CompleteRead(writer, systemEntity);
				}
			}
		}
	}
}
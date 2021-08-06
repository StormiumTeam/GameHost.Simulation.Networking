using System;
using System.Collections.Generic;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Revolution.Snapshot.Utilities;
using StormiumTeam.GameBase.Utility.Misc.EntitySystem;

namespace GameHost.Revolution.NetCode.Next.Batch
{
	public class SystemBatch : IBatch
	{
		public readonly PooledList<uint> SystemHandles = new();

		private BitBuffer[] buffers = Array.Empty<BitBuffer>();

		public SnapshotFrame? Baseline { get; set; }
		public SnapshotFrame  Target   { get; set; }
		public Entity         Client   { get; set; }

		public bool ClearSystemDataOnDuplicate { get; set; }

		public int PrepareBatch(int taskCount)
		{
			if (Target is null)
				throw new NullReferenceException(nameof(Target));

			if (SystemHandles.Count > buffers.Length)
			{
				var start = SystemHandles.Count;
				Array.Resize(ref buffers, SystemHandles.Count);

				for (; start < SystemHandles.Count; start++)
				{
					buffers[start] = new();
				}
			}

			return SystemHandles.Count;
		}

		public void Execute(int index, int maxUseIndex, int task, int taskCount)
		{
			var handle = SystemHandles[index];
			var buffer = buffers[index];
			buffer.Clear();

			var storage  = Target.SystemStorage[handle];
			var nextData = storage.Get<WrittenClientData>();
			var system   = storage.Get<ISnapshotSystem>();

			if (Baseline is { } baseline && baseline.SystemStorage.TryGetValue(handle, out var baselineStorage))
			{
				var otherSystem = baselineStorage.Get<ISnapshotSystem>();
				// maybe there should be a function named 'CompatibleWith(Entity storage)' to check if that system can use this storage?
				if (system.GetType() != otherSystem.GetType())
					throw new InvalidOperationException();

				system.WriteBuffer(buffer, storage, baselineStorage);
				if (Client != default)
				{
					var previousBytes = baselineStorage.Get<WrittenClientData>()
					                                   .GetClientData(Client);
					if (previousBytes.Count < buffer.Length)
						return;

					var bytes = nextData.GetClientData(Client);
					buffer.ToSpan(bytes.AddSpan(buffer.Length));
					if (previousBytes.Span.SequenceEqual(bytes.Span))
						buffer.Clear();
				}

				return;
			}

			system.WriteBuffer(buffer, storage, null);
			if (Client != default)
			{
				var bytes = nextData.GetClientData(Client)
				                    .AddSpan(buffer.Length);
				bytes.Clear();
				buffer.ToSpan(bytes);
			}
		}


		public void Complete(BitBuffer output)
		{
			uint previousHandle = default;
			for (var handle = 0u; handle < SystemHandles.Count; handle++)
			{
				var buffer = buffers[handle];
				if (buffer.Length == 0)
					return;

				output.AddUIntD4Delta(handle, previousHandle)
				      .AddBitBuffer(buffer);

				previousHandle = handle;
			}
		}
	}
}
using System;
using System.Collections.Generic;
using DefaultEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public class SnapshotHistory : IDisposable
	{
		private struct IsLockedTag
		{
		}

		private List<(uint line, Entity entity)> availableLines;
		private World                            world;

		private readonly int historySize;

		public SnapshotHistory(int historySize = 32)
		{
			this.historySize = historySize;

			availableLines = new(historySize);
			world          = new(historySize * 8);
		}

		public void TryRecycleUnused()
		{
			if (availableLines.Count < historySize)
				return;

			for (var i = 0; i < availableLines.Count; i++)
			{
				var (_, entity) = availableLines[i];
				if (entity.Has<IsLockedTag>())
					return;

				availableLines.RemoveAt(i--);
			}
		}

		public Entity GetLine(uint line)
		{
			Entity GetOrCreate()
			{
				foreach (var (baseline, baselineEntity) in availableLines)
					if (line == baseline)
						return baselineEntity;

				var entity = world.CreateEntity();
				availableLines.Add((line, entity));
				return entity;
			}

			return GetOrCreate();
		}

		/// <summary>
		/// Remove the possibility for a line to be removed if <see cref="isLocked"/> is true.
		/// </summary>
		/// <param name="target">The line</param>
		/// <param name="isLocked">Whether or not this line should be locked</param>
		public void SetLocked(uint target, bool isLocked)
		{
			foreach (var (line, entity) in availableLines)
				if (line == target)
				{
					if (isLocked)
						entity.Set<IsLockedTag>();
					else
						entity.Remove<IsLockedTag>();
				}
		}

		public void Dispose()
		{
			world.Dispose();
		}
	}
}
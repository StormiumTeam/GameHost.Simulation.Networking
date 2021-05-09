using System;
using System.Collections.Generic;
using DefaultEcs;

namespace GameHost.Revolution.NetCode.Next
{
	public class SnapshotHistory : IDisposable
	{
		private List<(uint line, Entity)> availableLines;
		private World                     world;
		
		public SnapshotHistory(int historySize = 32, ISnapshot snapshot = null)
		{
			availableLines = new(historySize);
			world          = new(historySize * 4);
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

		public void Dispose()
		{
			world.Dispose();
		}
	}
}
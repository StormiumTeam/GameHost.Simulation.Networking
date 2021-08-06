using System;
using StormiumTeam.GameBase.Utility.Misc.EntitySystem;

namespace GameHost.Revolution.NetCode.Next
{
	public class SingleThreadedBatchRunner : IBatchRunner
	{
		public bool IsWarmed()
		{
			return true;
		}

		public bool IsCompleted(BatchRequest request)
		{
			return true;
		}

		public BatchRequest Queue(IBatch batch)
		{
			var use = batch.PrepareBatch(1);
			for (var i = 0; i < use; i++)
			{
				batch.Execute(i, use, 1, 1);
			}

			return default;
		}

		public void TryDivergeRequest(BatchRequest request, bool canDivergeOnMainThread)
		{
			throw new InvalidOperationException("batches should have been completed before this method is called!");
		}
	}
}
using System;
using System.Threading;
using GameHost.Core.Threading;

namespace GameHost.Revolution.Snapshot.Utilities
{
	// wip file
	// this will be used to replace tasks (perhaps) with zero gc alloc
	// but since UniTask seems to work well, it seems that it never gonna get replaced...
	// (until I find a problem in it, then I'll continue working on this 'file')
	
	public abstract class ThreadTask
	{
		public abstract bool IsCompleted { get; }
	}

	public class ParallelThreadTask : ThreadTask
	{
		public int Counter;
		public int Max;

		public override bool IsCompleted => Counter >= Max;
	}

	public class ThreadCollection : IDisposable
	{
		private readonly Thread[]     threads;
		private readonly IScheduler[] schedulers;

		public ThreadCollection(int threadCount)
		{
			threads    = new Thread[threadCount];
			schedulers = new IScheduler[threadCount];
			for (var i = 0; i != threads.Length; i++)
			{
				schedulers[i] = new Scheduler();
				
				threads[i] = new Thread(Update);
				threads[i].Start(i);
			}
		}

		private void Update(object? state)
		{
			var threadIndex = (int) state;
		}

		/*public ThreadTask ScheduleParallel(int start, int count)
		{
			var task = new ParallelThreadTask();


			var workerCount = count / threads.Length;
			for (var i = 0; i != workerCount; i++)
			{
				
			}
		}*/

		public void Dispose()
		{
		}
	}
}
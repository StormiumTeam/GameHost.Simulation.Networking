using Revolution;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Networking.Transport.Utilities;
using UnityEngine;

namespace Unity.NetCode
{
	public struct Predicted<T> : IComponentData
	{
		public uint AppliedTick;
		public uint PredictionStartTick;
	}

	[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
	public class ComponentUpdateSystemDirect<TComponent, TSnapshot> : ComponentUpdateSystemDirect<TComponent, TSnapshot, DefaultSetup>
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>
	{
	}

	[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
	public class ComponentUpdateSystemDirect<TComponent, TSnapshot, TSetup> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>
		where TSetup : struct, ISetup
	{
		[BurstCompile]
		private struct JobDirect : IJobForEach_BC<TSnapshot, TComponent>
		{
			[ReadOnly]
			public DeserializeClientData JobData;

			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshot, ref TComponent component)
			{
				if (snapshot.Length > 0)
					snapshot[snapshot.Length - 1].SynchronizeTo(ref component, in JobData);
			}
		}

		private EntityQuery           m_UpdateQuery;
		private SnapshotReceiveSystem m_ReceiveSystem;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_UpdateQuery   = GetEntityQuery(typeof(TSnapshot), typeof(TComponent));
			m_ReceiveSystem = World.GetOrCreateSystem<SnapshotReceiveSystem>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			return m_ReceiveSystem.AddDependency(new JobDirect
			{
				JobData = m_ReceiveSystem.JobData
			}.Schedule(m_UpdateQuery, inputDeps));
		}
	}

	// -------------------------------------------------------------- //
	// INTERPOLATED
	// -------------------------------------------------------------- //

	[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
	public class ComponentUpdateSystemInterpolated<TComponent, TSnapshot> : ComponentUpdateSystemInterpolated<TComponent, TSnapshot, DefaultSetup>
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>, IInterpolatable<TSnapshot>
	{
		public ComponentUpdateSystemInterpolated() : base(false)
		{
		}
		
		public ComponentUpdateSystemInterpolated(bool isPredicted) : base(isPredicted)
		{
		}
	}

	[UpdateInGroup(typeof(GhostUpdateSystemGroup))]
	public class ComponentUpdateSystemInterpolated<TComponent, TSnapshot, TSetup> : JobComponentSystem
		where TComponent : struct, IComponentData
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IInterpolatable<TSnapshot>
		where TSetup : struct, ISetup
	{
		protected readonly bool IsPredicted;

		private EntityQuery m_RequiredQuery;
		private EntityQuery                 m_ComponentWithoutPrediction;
		private SnapshotReceiveSystem       m_ReceiveSystem;
		private ClientSimulationSystemGroup m_ClientGroup;

		private uint m_LastPredictTick;

		public ComponentUpdateSystemInterpolated() : this(false)
		{
		}

		public ComponentUpdateSystemInterpolated(bool isPredicted)
		{
			IsPredicted = isPredicted;
		}

		protected override void OnCreate()
		{
			base.OnCreate();

			Debug.Log("Created!");
			m_RequiredQuery = IsPredicted
				? GetEntityQuery(new EntityQueryDesc {All = new ComponentType[] {typeof(TSnapshot), typeof(TComponent), typeof(Predicted<TSnapshot>)}})
				: GetEntityQuery(new EntityQueryDesc {All = new ComponentType[] {typeof(TSnapshot), typeof(TComponent)}});

			if (IsPredicted)
			{
				m_ComponentWithoutPrediction = GetEntityQuery(new EntityQueryDesc
				{
					All  = new ComponentType[] {typeof(TSnapshot), typeof(TComponent)},
					None = new ComponentType[] {typeof(Predicted<TSnapshot>)}
				});
			}

			m_ReceiveSystem = World.GetOrCreateSystem<SnapshotReceiveSystem>();
			m_ClientGroup   = World.GetOrCreateSystem<ClientSimulationSystemGroup>();
		}

		protected override JobHandle OnUpdate(JobHandle inputDeps)
		{
			if (IsPredicted)
			{
				if (!m_ComponentWithoutPrediction.IsEmptyIgnoreFilter)
				{
					EntityManager.AddComponent(m_ComponentWithoutPrediction, typeof(Predicted<TSnapshot>));
				}
				
				var ghostPredictionGroup = World.GetExistingSystem<GhostPredictionSystemGroup>();
				var jobData          = m_ReceiveSystem.JobData;
				var targetTick       = m_ClientGroup.InterpolationTick;
				var lastPredictTick  = m_LastPredictTick;
				var minPredictedTick = ghostPredictionGroup.OldestPredictedTick;

				inputDeps = new _PredictedJob
				{
					jobData          = jobData,
					targetTick       = targetTick,
					lastPredictTick  = lastPredictTick,
					minPredictedTick = minPredictedTick
				}.Schedule(m_RequiredQuery, inputDeps);
				
				m_LastPredictTick = m_ClientGroup.ServerTick;
				if (m_ClientGroup.ServerTickFraction < 1)
					m_LastPredictTick = 0;
				
				ghostPredictionGroup.AddPredictedTickWriter(inputDeps);
			} 
			else
			{
				var jobData    = m_ReceiveSystem.JobData;
				var targetTick = m_ClientGroup.InterpolationTick;
				
				inputDeps = new _InterpolatedJob
				{
					jobData    = jobData,
					targetTick = targetTick
				}.Schedule(m_RequiredQuery, inputDeps);
			}
			
			return inputDeps;
		}

		//[BurstCompile]
		private struct _InterpolatedJob : IJobForEach_BC<TSnapshot, TComponent>
		{
			[ReadOnly]
			public DeserializeClientData jobData;

			public uint targetTick;

			public void Execute(DynamicBuffer<TSnapshot> snapshots, ref TComponent component)
			{
				if (!snapshots.GetDataAtTick(targetTick, out var snapshotData))
				{
					Debug.Log($"{snapshots.GetLastBaselineReadOnly().Tick} -> {typeof(TComponent)}");
					return;
				}
				
				snapshotData.SynchronizeTo(ref component, jobData);
			}
		}

		//[BurstCompile]
		private struct _PredictedJob : IJobForEach_BCC<TSnapshot, TComponent, Predicted<TSnapshot>>
		{
			[ReadOnly]
			public DeserializeClientData jobData;

			public uint targetTick;
			public uint lastPredictTick;

			[NativeDisableParallelForRestriction]
			public NativeArray<uint> minPredictedTick;

			[NativeSetThreadIndex]
			public int ThreadIndex;

			public void Execute([ReadOnly] DynamicBuffer<TSnapshot> snapshots, ref TComponent component, ref Predicted<TSnapshot> predictedData)
			{
				snapshots.GetDataAtTick(targetTick, out var snapshotData);

				var lastPredictedTickInst = lastPredictTick;
				if (lastPredictedTickInst == 0 || predictedData.AppliedTick != snapshotData.Tick)
					lastPredictedTickInst = snapshotData.Tick;
				else if (!SequenceHelpers.IsNewer(lastPredictedTickInst, snapshotData.Tick))
					lastPredictedTickInst = snapshotData.Tick;
				if (minPredictedTick[ThreadIndex] == 0 || SequenceHelpers.IsNewer(minPredictedTick[ThreadIndex], lastPredictedTickInst))
					minPredictedTick[ThreadIndex] = lastPredictedTickInst;

				predictedData = new Predicted<TSnapshot> {AppliedTick = snapshotData.Tick, PredictionStartTick = lastPredictedTickInst};
				if (lastPredictedTickInst != snapshotData.Tick)
				{
					return;
				}
				
				snapshotData.SynchronizeTo(ref component, jobData);
			}
		}
	}
}
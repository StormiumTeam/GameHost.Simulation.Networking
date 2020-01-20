using System;
using Unity.Burst;
using Unity.Entities;

namespace Revolution
{
	[Flags]
	public enum DeltaChangeType
	{
		Invalid   = 0,
		Chunk     = 1,
		Component = 2,
		Both      = 3
	}

	public interface ISnapshotDelta<in TSnapshot>
		where TSnapshot : ISnapshotDelta<TSnapshot>
	{
		bool DidChange(TSnapshot baseline);
	}

	[BurstCompile]
	public abstract class ComponentSnapshotSystemDelta<TComponent, TSnapshot, TSetup> : ComponentSnapshotSystemBase
	<
		TComponent,
		TSnapshot,
		TSetup,
		ComponentSnapshotSystemDelta<TComponent, TSnapshot, TSetup>.SharedData
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, TSetup>, IRwSnapshotComplement<TSnapshot>, ISnapshotDelta<TSnapshot>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISetup
	{
		public override string ToString()
		{
			return $"ComponentSnapshotSystemDelta<{typeof(TComponent)}>";
		}
		
		public virtual DeltaChangeType DeltaType => DeltaChangeType.Component;

		[BurstCompile]
		public static void Serialize(ref SerializeParameters parameters)
		{
			var sharedData = GetShared();

			ref var clientData = ref parameters.GetClientData();
			ref var stream     = ref parameters.GetStream();

			var tick   = clientData.Tick;
			var chunks = parameters.ChunksToSerialize;

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;

			bool success;
			if (!clientData.TryGetSnapshot(0, out var clientSnapshot)) throw new InvalidOperationException();

			ref var systemClientData = ref clientSnapshot.TryGetSystemData<ClientData>(parameters.SystemId, out success);
			if (!success)
			{
				systemClientData         = ref clientSnapshot.AllocateSystemData<ClientData>(parameters.SystemId);
				systemClientData.Version = 0;
			}

			GhostSnapshot ghostSnapshot;
			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(clientData.GhostType);

				var shouldSkip = false;
				if (deltaOnChunk)
				{
					shouldSkip = !chunk.DidChange(sharedData.ComponentTypeArch, systemClientData.Version);
					stream.WriteBitBool(shouldSkip);
					stream.WritePackedUIntDelta((uint) chunk.Count, previousChunkCount, clientData.NetworkCompressionModel);

					previousChunkCount = (uint) chunk.Count;
				}

				if (shouldSkip)
					continue;

				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!clientData.TryGetSnapshot(ghostArray[ent].Value, out ghostSnapshot)) throw new InvalidOperationException("A ghost should have a snapshot.");

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TSnapshot>(parameters.SystemId, out success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TSnapshot>(parameters.SystemId);
						baseline = default; // always set to default values!
					}

					var newSnapshot = default(TSnapshot);
					newSnapshot.Tick = tick;
					newSnapshot.SynchronizeFrom(componentArray[ent], in sharedData.SetupData, in clientData);

					// If we must check for delta change on components and
					// If the snapshot didn't changed since the previous baseline and
					// If the ghost using the snapshot isn't new then we can skip the serialize operation.
					if (deltaOnComponent)
					{
						// no change? skip
						if (!newSnapshot.DidChange(baseline) && success)
						{
							stream.WriteBitBool(true);
							continue;
						}

						// don't skip
						stream.WriteBitBool(false);
					}

					newSnapshot.WriteTo(stream, ref baseline, clientData.NetworkCompressionModel);

					baseline = newSnapshot;
				}
			}

			systemClientData.Version = sharedData.SystemVersion;
		}

		[BurstCompile]
		public static void Deserialize(ref DeserializeParameters parameters)
		{
			var sharedData = GetShared();
			var ghostArray = parameters.GhostsToDeserialize;

			var clientData = parameters.GetClientData();

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;
			var checkChunkSkipIn   = 0;
			for (int ent = 0, length = ghostArray.Length; ent < length; ent++, checkChunkSkipIn--)
			{
				bool shouldSkip;
				if (deltaOnChunk && checkChunkSkipIn <= 0)
				{
					shouldSkip         = parameters.Stream.ReadBitBool(ref parameters.Ctx);
					previousChunkCount = parameters.Stream.ReadPackedUIntDelta(ref parameters.Ctx, previousChunkCount, clientData.NetworkCompressionModel);

					if (shouldSkip)
					{
						ent += (int) previousChunkCount - 1;
						continue;
					}

					checkChunkSkipIn = (int) previousChunkCount;
				}

				if (deltaOnComponent)
				{
					shouldSkip = parameters.Stream.ReadBitBool(ref parameters.Ctx);

					if (shouldSkip)
						continue;
				}

				var     snapshotArray = sharedData.SnapshotFromEntity[clientData.GhostToEntityMap[ghostArray[ent]]];
				ref var baseline      = ref snapshotArray.GetLastBaseline();

				if (snapshotArray.Length >= SnapshotHistorySize)
					snapshotArray.RemoveAt(0);

				var newSnapshot = default(TSnapshot);
				newSnapshot.Tick = clientData.Tick;
				newSnapshot.ReadFrom(ref parameters.Ctx, parameters.Stream, ref baseline, clientData.NetworkCompressionModel);

				snapshotArray.Add(newSnapshot);
			}
		}

		protected override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		internal override void SystemBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.Delta             = DeltaType;
			sharedData.SystemVersion     = GlobalSystemVersion - 1;
			sharedData.ComponentTypeArch = GetArchetypeChunkComponentType<TComponent>(true);
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);

			SetEmptySafetyHandle(ref sharedData.ComponentTypeArch);
		}

		internal override void SystemBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetBufferFromEntity<TSnapshot>();
			SetEmptySafetyHandle(ref snapshotBuffer);

			ref var sharedData = ref GetShared();
			sharedData.Delta              = DeltaType;
			sharedData.SnapshotFromEntity = snapshotBuffer;
		}

		public struct SharedData
		{
			public TSetup SetupData;

			public uint            SystemVersion;
			public DeltaChangeType Delta;

			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public BufferFromEntity<TSnapshot>             SnapshotFromEntity;
		}

		private struct ClientData
		{
			public uint Version;
		}
	}

	public abstract class ComponentSnapshotSystemDelta<TComponent, TSnapshot> : ComponentSnapshotSystemDelta
	<
		TComponent,
		TSnapshot,
		DefaultSetup
	>
		where TSnapshot : struct, ISnapshotData<TSnapshot>, ISynchronizeImpl<TComponent, DefaultSetup>, IRwSnapshotComplement<TSnapshot>, ISnapshotDelta<TSnapshot>
		where TComponent : struct, IComponentData
	{
	}
}
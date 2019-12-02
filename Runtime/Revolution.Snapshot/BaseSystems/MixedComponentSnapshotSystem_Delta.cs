using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;

namespace Revolution
{

	[BurstCompile]
	public abstract class MixedComponentSnapshotSystem_Delta<TComponent, TSetup> : EntitySerializerComponent<MixedComponentSnapshotSystem_Delta<TComponent, TSetup>,
		TComponent,
		MixedComponentSnapshotSystem_Delta<TComponent, TSetup>.SharedData>
		where TComponent : struct, IComponentData, IReadWriteComponentSnapshot<TComponent, TSetup>, ISnapshotDelta<TComponent>
		where TSetup : struct, ISetup
	{
		public struct SharedData
		{
			public TSetup SetupData;

			public uint            SystemVersion;
			public DeltaChangeType Delta;

			public ArchetypeChunkComponentType<TComponent> ComponentTypeArch;
			public ComponentDataFromEntity<TComponent>     SnapshotFromEntity;
		}

		private struct ClientData
		{
			public uint Version;
		}

		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp)
			{
				[0] = typeof(TComponent)
			};

		[BurstCompile]
		public static void Serialize(uint systemId, ref SerializeClientData jobData, ref DataStreamWriter writer)
		{
			var sharedData = GetShared();
			var chunks     = GetSerializerChunkData().Array;

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;

			bool success;
			if (!jobData.TryGetSnapshot(0, out var clientSnapshot))
			{
				throw new InvalidOperationException();
			}

			ref var clientData = ref clientSnapshot.TryGetSystemData<ClientData>(systemId, out success);
			if (!success)
			{
				clientData         = ref clientSnapshot.AllocateSystemData<ClientData>(systemId);
				clientData.Version = 0;
			}

			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(jobData.GhostType);

				var shouldSkip = false;
				if (deltaOnChunk)
				{
					shouldSkip = !chunk.DidChange(sharedData.ComponentTypeArch, clientData.Version);
					writer.WriteBitBool(shouldSkip);
					writer.WritePackedUIntDelta((uint) chunk.Count, previousChunkCount, jobData.NetworkCompressionModel);

					previousChunkCount = (uint) chunk.Count;
				}

				if (shouldSkip)
					continue;

				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					if (!jobData.TryGetSnapshot(ghostArray[ent].Value, out var ghostSnapshot))
					{
						throw new InvalidOperationException("A ghost should have a snapshot.");
					}

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TComponent>(systemId, out success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TComponent>(systemId);
						baseline = default; // always set to default values!
					}

					var component = componentArray[ent];

					// If we must check for delta change on components and
					// If the snapshot didn't changed since the previous baseline and
					// If the ghost using the snapshot isn't new then we can skip the serialize operation.
					if (deltaOnComponent)
					{
						// no change? skip
						if (!component.DidChange(baseline) && success)
						{
							writer.WriteBitBool(true);
							continue;
						}

						// don't skip
						writer.WriteBitBool(false);
					}

					component.WriteTo(writer, ref baseline, sharedData.SetupData, jobData);
					baseline = component;
				}
			}

			clientData.Version = sharedData.SystemVersion;
		}

		[BurstCompile]
		public static void Deserialize(uint systemId, uint tick, ref DeserializeClientData jobData, ref DataStreamReader reader, ref DataStreamReader.Context ctx)
		{
			var sharedData = GetShared();
			var ghostArray = GetDeserializerGhostData().Array;

			var deltaOnChunk     = (sharedData.Delta & DeltaChangeType.Chunk) != 0;
			var deltaOnComponent = (sharedData.Delta & DeltaChangeType.Component) != 0;

			var previousChunkCount = 0u;
			var checkChunkSkipIn   = 0;
			for (int ent = 0, length = ghostArray.Length; ent < length; ent++, checkChunkSkipIn--)
			{
				bool shouldSkip;
				if (deltaOnChunk && checkChunkSkipIn <= 0)
				{
					shouldSkip         = reader.ReadBitBool(ref ctx);
					previousChunkCount = reader.ReadPackedUIntDelta(ref ctx, previousChunkCount, jobData.NetworkCompressionModel);

					if (shouldSkip)
					{
						ent += (int) previousChunkCount - 1;
						continue;
					}

					checkChunkSkipIn = (int) previousChunkCount;
				}

				if (deltaOnComponent)
				{
					shouldSkip = reader.ReadBitBool(ref ctx);

					if (shouldSkip)
						continue;
				}

				var entity      = jobData.GhostToEntityMap[ghostArray[ent]];
				var baseline    = sharedData.SnapshotFromEntity[entity];
				var newSnapshot = default(TComponent);

				newSnapshot.ReadFrom(ref ctx, reader, ref baseline, jobData);

				sharedData.SnapshotFromEntity[entity] = newSnapshot;
			}
		}

		public virtual DeltaChangeType DeltaType => DeltaChangeType.Both;

		protected override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		public override void OnBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.ComponentTypeArch  = GetArchetypeChunkComponentType<TComponent>(true);
			sharedData.SnapshotFromEntity = GetComponentDataFromEntity<TComponent>();
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
			
			SetEmptySafetyHandle(ref sharedData.ComponentTypeArch);
		}

		public override void OnBeginDeserialize(Entity entity)
		{
			var snapshotBuffer = GetComponentDataFromEntity<TComponent>();
#if ENABLE_UNITY_COLLECTIONS_CHECKS
			SafetyUtility.Replace(ref snapshotBuffer, SafetyHandle);
#endif

			ref var sharedData = ref GetShared();
			sharedData.SnapshotFromEntity = snapshotBuffer;
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
		}
	}

	public abstract class MixedComponentSnapshotSystem_Delta<TComponent> : MixedComponentSnapshotSystem_Delta
	<
		TComponent,
		DefaultSetup
	>
		where TComponent : struct, IReadWriteComponentSnapshot<TComponent, DefaultSetup>, ISnapshotDelta<TComponent>
	{
	}
}
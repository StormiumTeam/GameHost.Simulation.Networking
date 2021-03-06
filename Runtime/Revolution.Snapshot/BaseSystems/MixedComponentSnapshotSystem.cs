using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine.Profiling;

namespace Revolution
{
	public interface IReadWriteComponentSnapshot<T, TSetup> : IComponentData
		where T : struct, IReadWriteComponentSnapshot<T, TSetup>
	{
		void WriteTo(DataStreamWriter              writer, ref T            baseline, TSetup setup,    SerializeClientData   jobData);
		void ReadFrom(ref DataStreamReader.Context ctx,    DataStreamReader reader,   ref T  baseline, DeserializeClientData jobData);
	}

	public interface IReadWriteComponentSnapshot<T> : IReadWriteComponentSnapshot<T, DefaultSetup>
		where T : struct, IReadWriteComponentSnapshot<T, DefaultSetup>
	{
	}

	[BurstCompile]
	public abstract class MixedComponentSnapshotSystem<TComponent, TSetup> : EntitySerializerComponent<MixedComponentSnapshotSystem<TComponent, TSetup>,
		TComponent,
		MixedComponentSnapshotSystem<TComponent, TSetup>.SharedData>
		where TComponent : struct, IComponentData, IReadWriteComponentSnapshot<TComponent, TSetup>
		where TSetup : struct, ISetup
	{
		public override string ToString()
		{
			return $"MixedComponentSnapshotSystemBasic<{typeof(TComponent)}>";
		}
		
		public override NativeArray<ComponentType> EntityComponents =>
			new NativeArray<ComponentType>(1, Allocator.Temp)
			{
				[0] = typeof(TComponent)
			};

		[BurstCompile]
		public static void Serialize(ref SerializeParameters parameters)
		{
			var sharedData = GetShared();
			
			ref var clientData = ref parameters.GetClientData();
			ref var stream     = ref parameters.GetStream();

			var tick   = clientData.Tick;
			var chunks = parameters.ChunksToSerialize;
			
			for (int c = 0, length = chunks.Length; c < length; c++)
			{
				var chunk          = chunks[c];
				var componentArray = chunk.GetNativeArray(sharedData.ComponentTypeArch);
				var ghostArray     = chunk.GetNativeArray(clientData.GhostType);

				bool          success;
				GhostSnapshot ghostSnapshot;
				for (int ent = 0, entityCount = chunk.Count; ent < entityCount; ent++)
				{
					ghostSnapshot = clientData.GetSnapshot(ghostArray[ent].Value);

					ref var baseline = ref ghostSnapshot.TryGetSystemData<TComponent>(parameters.SystemId, out success);
					if (!success)
					{
						baseline = ref ghostSnapshot.AllocateSystemData<TComponent>(parameters.SystemId);
						baseline = default; // always set to default values!
					}

					componentArray[ent].WriteTo(stream, ref baseline, sharedData.SetupData, clientData);
					baseline = componentArray[ent];
				}
			}
		}

		[BurstCompile]
		public static void Deserialize(ref DeserializeParameters parameters)
		{
			var sharedData = GetShared();
			var ghostArray = parameters.GhostsToDeserialize;

			var clientData = parameters.GetClientData();

			for (int ent = 0, length = ghostArray.Length; ent < length; ent++)
			{
				var entity      = clientData.GhostToEntityMap[ghostArray[ent]];
				var baseline    = sharedData.ComponentFromEntity[entity];
				var newSnapshot = default(TComponent);

				newSnapshot.ReadFrom(ref parameters.Ctx, parameters.Stream, ref baseline, clientData);

				sharedData.ComponentFromEntity[entity] = newSnapshot;
			}
		}

		protected override void GetDelegates(out BurstDelegate<OnSerializeSnapshot> onSerialize, out BurstDelegate<OnDeserializeSnapshot> onDeserialize)
		{
			onSerialize   = new BurstDelegate<OnSerializeSnapshot>(Serialize);
			onDeserialize = new BurstDelegate<OnDeserializeSnapshot>(Deserialize);
		}

		public override void OnBeginSerialize(Entity entity)
		{
			ref var sharedData = ref GetShared();
			sharedData.ComponentTypeArch = GetComponentTypeHandle<TComponent>(true);
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
			sharedData.ComponentFromEntity = snapshotBuffer;
			sharedData.SetupData.BeginSetup(this
#if ENABLE_UNITY_COLLECTIONS_CHECKS
				, SafetyHandle
#endif
			);
		}

		public struct SharedData
		{
			public TSetup                                  SetupData;
			public ComponentTypeHandle<TComponent> ComponentTypeArch;
			public ComponentDataFromEntity<TComponent>     ComponentFromEntity;
		}
	}
}
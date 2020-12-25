using System;
using System.Collections.Generic;
using DefaultEcs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.TabEcs.Interfaces;
using JetBrains.Annotations;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public abstract class DeltaSnapshotSerializerBase<TSnapshot, TComponent> : DeltaSnapshotSerializerBase<TSnapshot, TComponent, EmptySnapshotSetup>
		where TSnapshot : struct, ISnapshotData, IReadWriteSnapshotData<TSnapshot>, ISnapshotSyncWithComponent<TComponent>
		where TComponent : struct, IComponentData
	{
		protected DeltaSnapshotSerializerBase([NotNull] ISnapshotInstigator instigator, [NotNull] Context ctx) : base(instigator, ctx)
		{
		}
	}
		
	public abstract class DeltaSnapshotSerializerBase<TSnapshot, TComponent, TSetup> : SerializerBase
		where TSnapshot : struct, ISnapshotData, IReadWriteSnapshotData<TSnapshot, TSetup>, ISnapshotSyncWithComponent<TComponent, TSetup>
		where TComponent : struct, IComponentData
		where TSetup : struct, ISnapshotSetupData
	{
		private static readonly Action<Entity> setSerialize   = c => c.Set<SerializeInitialData>();
		private static readonly Action<Entity> setDeserialize = c => c.Set<DeserializeInitialData>();

		#region Settings

		/// <summary>
		///     Whether or not the component should be directly updated instead of using the snapshot buffer.
		/// </summary>
		/// <remarks>
		///	Default True
		/// </remarks>
		public bool DirectComponentSettings;

		/// <summary>
		/// Whether or not we should add the deserialized snapshot into the snapshot buffer
		/// </summary>
		/// <remarks>
		///	Default True
		/// </remarks>
		public bool AddToBufferSettings;

		/// <summary>
		/// Whether or not the data should still be written to the buffer (if <see cref="AddToBufferSettings"/> is true) if the entity is ignored (owner reason)
		/// </summary>
		/// <remarks>
		///	Default False
		/// </remarks>
		public bool ForceToBufferIfEntityIgnoredSettings;

		#endregion

		private readonly Dictionary<ISnapshotInstigator, InstigatorData> instigatorDataMap = new();

		/*public override bool SynchronousSerialize   => true;
		public override bool SynchronousDeserialize => true;*/

		protected TSetup setup;

		public DeltaSnapshotSerializerBase(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
		{
			// The reason why we both directly set the component and buffer is that the game client (eg: Unity client) will use the buffer for interpolated data when available...
			// It doesn't really make sense to interpolate data that isn't visible to the end-user.
			//
			// If you require interpolation (or something like prediction) on the simulation client, then you should use disable this and run prediction stuff
			DirectComponentSettings              = true;
			AddToBufferSettings                  = true;
			ForceToBufferIfEntityIgnoredSettings = false;

			setup = new TSetup();
		}

		protected override void OnDependenciesResolved(IEnumerable<object> deps)
		{
			base.OnDependenciesResolved(deps);
			
			setup.Create(this);
		}

		protected override ISerializerArchetype GetSerializerArchetype()
		{
			if (System.Id == 0)
				throw new InvalidOperationException("This serializer should have been set with a system.");

			var component = GameWorld.AsComponentType<TComponent>();
			var snapshot  = GameWorld.AsComponentType<TSnapshot>();
			return new SimpleSerializerArchetype(this, GameWorld, snapshot, new[] {component}, Array.Empty<ComponentType>());
		}
		
		public override void OnReset(ISnapshotInstigator instigator)
		{
			if (instigatorDataMap.TryGetValue(instigator, out var baseline))
				baseline.BaselineArray.AsSpan().Clear();
		}

		public override void UpdateMergeGroup(ReadOnlySpan<Entity> clients, MergeGroupCollection collection)
		{
			// The goal is to merge clients into two groups:
			// - clients that have the initial data
			// - clients that never had the initial data.
			//
			// !!! The clients you get possess the same entity set. (eg: if one client has one more entity with a valid archetype, it will be in another group no matter what)
			//
			// You're free to modify it to have a better merging system:
			// - For example you could occlude and ignore serialization based on the client camera.
			// - You could also lower the precision or reduce data based on the distance between client or other stuff...
			//
			// If you wish to keep this basic merging system and add ignore some clients,
			// just do 'collection.SetToGroup(client, null)'.
			// The serializer only call based on groups, so if this client isn't attached to any, it will never get called for it.
			foreach (var client in clients)
			{
				var thisHasData                                           = client.Has<SerializeInitialData>();
				
				if (!collection.TryGetGroup(client, out var group)) group = collection.CreateGroup();
				foreach (var other in clients)
				{
					if (other.Has<SerializeInitialData>() != thisHasData)
						continue;

					collection.SetToGroup(other, group);
				}
			}
		}

		protected override void PrepareGlobal()
		{
			base.PrepareGlobal();

			if (!instigatorDataMap.TryGetValue(Instigator, out var data))
				instigatorDataMap[Instigator] = data = new InstigatorData();
		}

		protected override void OnSerialize(BitBuffer bitBuffer, SerializationParameters parameters, MergeGroup group, ReadOnlySpan<GameEntityHandle> entities)
		{
			setup.Begin(true);

			var         hadInitialData = group.Storage.Has<SerializeInitialData>();
			TSnapshot[] writeArray;

			ref var __readArray = ref instigatorDataMap[Instigator].BaselineArray;
			GetColumn(ref __readArray, entities);

			var readArray = __readArray;

			if (!hadInitialData)
			{
				writeArray = readArray = new TSnapshot[entities.Length];
				parameters.Post.Schedule(setSerialize, group.Storage, default);
				foreach (var client in group.Entities)
					parameters.Post.Schedule(setSerialize, client, default);
			}
			else
				writeArray = readArray;

			var accessor = new ComponentDataAccessor<TComponent>(GameWorld);
			for (var ent = 0; ent < entities.Length; ent++)
			{
				var self     = entities[ent];
				var snapshot = default(TSnapshot);
				snapshot.Tick = parameters.Tick;
				snapshot.FromComponent(accessor[self], setup);
				snapshot.Serialize(bitBuffer, readArray[ent], setup);
				//snapshot.Serialize(bitBuffer, default, setup);

				writeArray[ent] = snapshot;
			}

			__readArray = writeArray;
		}
		
		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISerializer.RefData refData)
		{
			setup.Begin(false);
			
			ref var baselineArray = ref instigatorDataMap[Instigator].BaselineArray;

			GetColumn(ref baselineArray, refData.Self);

			var dataAccessor   = new ComponentDataAccessor<TComponent>(GameWorld);
			var bufferAccessor = new ComponentBufferAccessor<TSnapshot>(GameWorld);

			// The code is a bit less complex here, since we assume that when we deserialize we do that for one client per instigator...
			var hadInitialData = Instigator.Storage.Has<DeserializeInitialData>();
			if (!hadInitialData)
			{
				Array.Fill(baselineArray, default);

				parameters.Post.Schedule(setDeserialize, Instigator.Storage, default);
			}

			for (var ent = 0; ent < refData.Self.Length; ent++)
			{
				var self = refData.Self[ent];

				ref var baseline = ref baselineArray[ent];

				var snapshot = default(TSnapshot);
				snapshot.Tick = parameters.Tick;
				snapshot.Deserialize(bitBuffer, baseline, setup);
				//snapshot.Deserialize(bitBuffer, default, setup);
				
				baseline = snapshot;

				// If we have been requested to add data to ignored entities, and those entities aren't null, do it.
				// The entity can be null (aka zero) if it doesn't exist. (This can happen if it got destroyed on our side, but not on the sender side)
				if (self.Id == 0)
					continue;

				if (refData.IgnoredSet[(int) refData.Snapshot[ent].Id])
				{
					if (ForceToBufferIfEntityIgnoredSettings && AddToBufferSettings)
					{
						bufferAccessor[self].Add(snapshot);
						if (bufferAccessor[self].Count > 32)
							bufferAccessor[self].RemoveAt(0);
					}
				}
				else
				{
					if (DirectComponentSettings)
						snapshot.ToComponent(ref dataAccessor[self], setup);
					if (AddToBufferSettings)
					{
						bufferAccessor[self].Add(snapshot);
						if (bufferAccessor[self].Count > 32)
							bufferAccessor[self].RemoveAt(0);
					}
				}
			}
		}

		/// <summary>
		///     Baseline that represent previous snapshot data of all entities.
		/// </summary>
		/// <remarks>
		///     The array is a column, automatically resized to the max number of !allocated! entities.
		/// </remarks>
		public class InstigatorData
		{
			public TSnapshot[] BaselineArray = Array.Empty<TSnapshot>();
		}

		public struct SerializeInitialData
		{
		}
		
		public struct DeserializeInitialData
		{
		}
	}
}
using System;
using System.Collections.Generic;
using System.Threading;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.TabEcs.Interfaces;
using JetBrains.Annotations;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public abstract class DeltaComponentSerializerBase<TComponent> : DeltaComponentSerializerBase<TComponent, EmptySnapshotSetup>
		where TComponent : struct, IComponentData, IReadWriteComponentData<TComponent, EmptySnapshotSetup>
	{
		protected DeltaComponentSerializerBase([NotNull] ISnapshotInstigator instigator, [NotNull] Context ctx) : base(instigator, ctx)
		{
		}
	}
		
	// Some small notes:
	// We don't access to previous component data for delta changes...
	// Instead we just do it like if it was a snapshot (so in case the simulation modify the data, it wouldn't corrupt delta change)
	
	public abstract class DeltaComponentSerializerBase<TComponent, TSetup> : SerializerBase
		where TComponent : struct, IComponentData, IReadWriteComponentData<TComponent, TSetup>
		where TSetup : struct, ISnapshotSetupData
	{
		private static readonly Action<Entity> setComponent = c => c.Set<InitialData>();

		#region Settings


		#endregion

		private readonly Dictionary<ISnapshotInstigator, InstigatorData> instigatorDataMap = new();

		public override bool SynchronousSerialize   => true;
		public override bool SynchronousDeserialize => true;

		protected TSetup setup;

		public DeltaComponentSerializerBase(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
		{
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
			return new SimpleSerializerArchetype(this, GameWorld, component, new[] {component}, Array.Empty<ComponentType>());
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
				var thisHasData = client.Has<InitialData>();
				
				if (!collection.TryGetGroup(client, out var group)) group = collection.CreateGroup();
				foreach (var other in clients)
				{
					if (other.Has<InitialData>() != thisHasData)
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

			var         hadInitialData = group.Storage.Has<InitialData>();
			TComponent[] writeArray, readArray;
			
			ref var __readArray = ref instigatorDataMap[Instigator].BaselineArray;
			GetColumn(ref __readArray, entities);

			readArray = __readArray;

			if (!hadInitialData)
			{
				writeArray = readArray = new TComponent[entities.Length];
				parameters.Post.Schedule(setComponent, group.Storage, default);
			}
			else
				writeArray = readArray;

			var accessor = new ComponentDataAccessor<TComponent>(GameWorld);
			for (var ent = 0; ent < entities.Length; ent++)
			{
				var self     = entities[ent];
				var snapshot = accessor[self];
				snapshot.Serialize(bitBuffer, readArray[ent], setup);

				writeArray[ent] = snapshot;
			}

			__readArray = writeArray;
		}

		private static void Set(int yes)
		{
			
		}

		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISerializer.RefData refData)
		{
			setup.Begin(false);
			
			ref var baselineArray = ref instigatorDataMap[Instigator].BaselineArray;

			GetColumn(ref baselineArray, refData.Self);

			var dataAccessor   = new ComponentDataAccessor<TComponent>(GameWorld);

			// The code is a bit less complex here, since we assume that when we deserialize we do that for one client per instigator...
			var hadInitialData = Instigator.Storage.Has<InitialData>();
			if (!hadInitialData)
			{
				Array.Fill(baselineArray, default);

				parameters.Post.Schedule(setComponent, Instigator.Storage, default);
			}

			for (var ent = 0; ent < refData.Self.Length; ent++)
			{
				var self = refData.Self[ent];

				ref var baseline = ref baselineArray[ent];

				var snapshot = dataAccessor[self];
				snapshot.Deserialize(bitBuffer, baseline, setup);
				
				baseline = snapshot;

				// If we have been requested to add data to ignored entities, and those entities aren't null, do it.
				// The entity can be null (aka zero) if it doesn't exist. (This can happen if it got destroyed on our side, but not on the sender side)
				if (self.Id == 0)
					continue;

				if (refData.IgnoredSet[(int) self.Id])
					continue;

				// all ok, set the component
				dataAccessor[self] = snapshot;
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
			public TComponent[] BaselineArray = Array.Empty<TComponent>();
		}

		public struct InitialData
		{
		}
	}
}
using System;
using System.Buffers;
using System.Collections.Generic;
using DefaultEcs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.TabEcs.Interfaces;
using JetBrains.Annotations;
using RevolutionSnapshot.Core.Buffers;
using StormiumTeam.GameBase.Utility.Misc;
using ZLogger;

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
		private static readonly Action<(Entity e, uint baseline)> setSerialize   = c => c.e.Set(new SerializeCurrentBaseline {Value   = c.baseline});
		private static readonly Action<(Entity e, uint baseline)> setDeserialize = c => c.e.Set(new DeserializeCurrentBaseline {Value = c.baseline});

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

		/// <summary>
		/// Check for difference between current and previous component data. If false, it will not write the component data
		/// </summary>
		public bool CheckDifferenceSettings;

		/// <summary>
		/// Check for the difference between previous and current data of this system. If false, nothing will be written.
		/// </summary>
		public EqualsWholeSnapshot CheckEqualsWholeSnapshotSettings;

		public enum EqualsWholeSnapshot
		{
			None,

			// The fastest if we're already checking difference with CheckDifferenceSettings (if this enum is selected, CheckDifferenceSettings will be forced to true)
			CheckWithComponentDifference,
			CheckWithLatestData
		}

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
			CheckDifferenceSettings              = true;
			CheckEqualsWholeSnapshotSettings     = EqualsWholeSnapshot.CheckWithComponentDifference;

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
			{
				foreach (var snapshots in baseline.Serialize.Values)
					ArrayPool<TSnapshot>.Shared.Return(snapshots);
				foreach (var snapshots in baseline.Deserialize.Values)
					ArrayPool<TSnapshot>.Shared.Return(snapshots);
				
				baseline.Serialize.Clear();
				baseline.Deserialize.Clear();
			}
		}

		public override void UpdateMergeGroup(ReadOnlySpan<Entity> clients, MergeGroupCollection collection)
		{
			foreach (var client in clients)
				if (!client.Has<SerializeCurrentBaseline>())
					client.Set(new SerializeCurrentBaseline {Value = 0});

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
				var baseline = client.Get<SerializeCurrentBaseline>();

				if (!collection.TryGetGroup(client, out var group)) group = collection.CreateGroup();
				foreach (var other in clients)
				{
					if (other.Get<SerializeCurrentBaseline>().Value != baseline.Value)
						continue;

					collection.SetToGroup(other, group);
				}
			}
		}

		protected override void PrepareGlobal(uint tick, uint baseline, int entityCount)
		{
			base.PrepareGlobal(tick, baseline, entityCount);

			if (!instigatorDataMap.TryGetValue(Instigator, out var data))
				instigatorDataMap[Instigator] = data = new InstigatorData();
			
			if (!data.Serialize.ContainsKey(0))
				data.Serialize[0] = ArrayPool<TSnapshot>.Shared.Rent(entityCount);
			if (!data.Deserialize.ContainsKey(0))
				data.Deserialize[0] = ArrayPool<TSnapshot>.Shared.Rent(entityCount);

			data.Serialize[tick]   = ArrayPool<TSnapshot>.Shared.Rent(entityCount);
			data.Deserialize[tick] = ArrayPool<TSnapshot>.Shared.Rent(entityCount);
		}

		protected override void OnSerialize(BitBuffer bitBuffer, SerializationParameters parameters, MergeGroup group, ReadOnlySpan<GameEntityHandle> entities)
		{
			setup.Begin(true);

			var currentBaseline = group.Storage.Get<SerializeCurrentBaseline>().Value;

			var serializeMap = instigatorDataMap[Instigator].Serialize;
			if (!serializeMap.TryGetValue(currentBaseline, out var readArray))
			{
				Logger.ZLogCritical("(Serialization) No readable baseline {0} found in system {1} !", currentBaseline, TypeExt.GetFriendlyName(GetType()));
				return;
			}

			if (!serializeMap.TryGetValue(parameters.Tick, out var writeArray))
			{
				Logger.ZLogCritical("(Serialization) No writable baseline {0} found in system {1} !", currentBaseline, TypeExt.GetFriendlyName(GetType()));
				return;
			}

			parameters.Post.Schedule(setSerialize, (group.Storage, parameters.Tick), default);
			foreach (var client in group.Entities)
				parameters.Post.Schedule(setSerialize, (client, parameters.Tick), default);

			var differentCount = 0;
			var accessor       = new ComponentDataAccessor<TComponent>(GameWorld);
			switch (CheckDifferenceSettings)
			{
				case true:
					for (var ent = 0; ent < entities.Length; ent++)
					{
						var snapshot = default(TSnapshot);
						snapshot.Tick = parameters.Tick;
						snapshot.FromComponent(accessor[entities[ent]], setup);
						if (UnsafeUtility.SameData(snapshot, readArray[ent]))
						{
							bitBuffer.AddBool(true);
							continue;
						}

						differentCount++;

						bitBuffer.AddBool(false);
						snapshot.Serialize(bitBuffer, readArray[ent], setup);
						writeArray[ent] = snapshot;
					}

					break;
				case false:
					for (var ent = 0; ent < entities.Length; ent++)
					{
						var snapshot = default(TSnapshot);
						snapshot.Tick = parameters.Tick;
						snapshot.FromComponent(accessor[entities[ent]], setup);
						if (CheckEqualsWholeSnapshotSettings == EqualsWholeSnapshot.CheckWithComponentDifference)
						{
							if (!UnsafeUtility.SameData(snapshot, readArray[ent]))
								differentCount++;
						}

						snapshot.Serialize(bitBuffer, readArray[ent], setup);
						writeArray[ent] = snapshot;
					}

					break;
			}

			if (differentCount == 0
			    && CheckEqualsWholeSnapshotSettings == EqualsWholeSnapshot.CheckWithComponentDifference
			    && !parameters.HadEntityUpdate)
			{
				bitBuffer.Clear();
			}
		}

		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISnapshotSerializerSystem.RefData refData)
		{
			setup.Begin(false);

			var currentBaseline = Instigator.Storage.Get<DeserializeCurrentBaseline>().Value;

			var serializeMap = instigatorDataMap[Instigator].Serialize;
			if (!serializeMap.TryGetValue(currentBaseline, out var baselineArray))
			{
				Logger.ZLogCritical("(Deserialization) No readable baseline {0} found in system {1} !", currentBaseline, TypeExt.GetFriendlyName(GetType()));
				return;
			}

			var dataAccessor   = new ComponentDataAccessor<TComponent>(GameWorld);
			var bufferAccessor = new ComponentBufferAccessor<TSnapshot>(GameWorld);

			parameters.Post.Schedule(setDeserialize, (Instigator.Storage, parameters.Tick), default);

			switch (CheckDifferenceSettings)
			{
				case true:
					for (var ent = 0; ent < refData.Self.Length; ent++)
					{
						ref var baseline = ref baselineArray[ent];
						if (bitBuffer.ReadBool())
						{
							// don't touch the baseline (except tick)
							baseline.Tick = parameters.Tick;
							continue;
						}

						var snapshot = default(TSnapshot);
						snapshot.Tick = parameters.Tick;
						snapshot.Deserialize(bitBuffer, baseline, setup);

						baseline = snapshot;
					}

					break;
				case false:
					for (var ent = 0; ent < refData.Self.Length; ent++)
					{
						ref var baseline = ref baselineArray[ent];
						var     snapshot = default(TSnapshot);
						snapshot.Tick = parameters.Tick;
						snapshot.Deserialize(bitBuffer, baseline, setup);

						baseline = snapshot;
					}

					break;
			}

			for (var ent = 0; ent < refData.Self.Length; ent++)
			{
				var self = refData.Self[ent];

				// If we have been requested to add data to ignored entities, and those entities aren't null, do it.
				// The entity can be null (aka zero) if it doesn't exist. (This can happen if it got destroyed on our side, but not on the sender side)
				if (self.Id == 0)
					continue;

				if (refData.IgnoredSet[(int) refData.Snapshot[ent].Id])
				{
					if (ForceToBufferIfEntityIgnoredSettings && AddToBufferSettings)
					{
						ref readonly var snapshot = ref baselineArray[ent];

						bufferAccessor[self].Add(snapshot);
						if (bufferAccessor[self].Count > 32)
							bufferAccessor[self].RemoveAt(0);
					}
				}
				else
				{
					ref readonly var snapshot = ref baselineArray[ent];

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
			public Dictionary<uint, TSnapshot[]> Serialize   = new();
			public Dictionary<uint, TSnapshot[]> Deserialize = new();
		}

		public struct SerializeCurrentBaseline
		{
			public uint Value;
		}

		public struct DeserializeCurrentBaseline
		{
			public uint Value;
		}
	}
}
﻿using System;
using System.Collections.Generic;
using System.Threading;
using DefaultEcs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;
using GameHost.Simulation.TabEcs.Interfaces;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public interface ISnapshotData : IComponentBuffer
	{
		uint Tick { get; set; }
	}

	public interface IReadWriteSnapshotData<T> : ISnapshotData
		where T : ISnapshotData
	{
		void Serialize(in   BitBuffer buffer, in T baseline);
		void Deserialize(in BitBuffer buffer, in T baseline);
	}

	public interface ISnapshotSyncWithComponent<TComponent> : ISnapshotData
	{
		void FromComponent(in TComponent component);
		void ToComponent(ref  TComponent component);
	}

	public abstract class DeltaComponentSerializerBase<TSnapshot, TComponent> : SerializerBase
		where TSnapshot : struct, ISnapshotData, IReadWriteSnapshotData<TSnapshot>, ISnapshotSyncWithComponent<TComponent>
		where TComponent : struct, IComponentData
	{
		private static readonly Action<Entity> setComponent = c => c.Set<InitialData>();

		#region Settings

		/// <summary>
		///     Whether or not the component should be directly updated instead of using the snapshot buffer.
		/// </summary>
		/// <remarks>
		///	Default False
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

		public override bool SynchronousSerialize   => true;
		public override bool SynchronousDeserialize => true;

		public DeltaComponentSerializerBase(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
		{
			DirectComponentSettings              = false;
			AddToBufferSettings                  = true;
			ForceToBufferIfEntityIgnoredSettings = false;
		}

		protected override ISerializerArchetype GetSerializerArchetype()
		{
			if (System.Id == 0)
				throw new InvalidOperationException("This serializer should have been set with a system.");

			var component = GameWorld.AsComponentType<TComponent>();
			var snapshot  = GameWorld.AsComponentType<TSnapshot>();
			return new SimpleSerializerArchetype(this, GameWorld, snapshot, new[] {component}, Array.Empty<ComponentType>());
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
			var         hadInitialData = group.Storage.Has<InitialData>();
			TSnapshot[] writeArray;

			ref var readArray = ref instigatorDataMap[Instigator].BaselineArray;
			GetColumn(ref readArray, entities);

			if (!hadInitialData)
			{
				writeArray = new TSnapshot[entities.Length];
				parameters.Post.Schedule(setComponent, group.Storage, default);
			}
			else
				writeArray = readArray;

			var accessor = new ComponentDataAccessor<TComponent>(GameWorld);
			foreach (var ent in entities)
			{
				var snapshot = default(TSnapshot);
				snapshot.Tick = parameters.Tick;
				snapshot.FromComponent(accessor[ent]);
				snapshot.Serialize(bitBuffer, readArray[ent.Id]);

				writeArray[ent.Id] = snapshot;
			}
		}

		private static void Set(int yes)
		{
			
		}

		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ReadOnlySpan<GameEntityHandle> entities, ReadOnlySpan<bool> ignoreSet)
		{
			ref var baselineArray = ref instigatorDataMap[Instigator].BaselineArray;

			GetColumn(ref baselineArray, entities);

			var dataAccessor   = new ComponentDataAccessor<TComponent>(GameWorld);
			var bufferAccessor = new ComponentBufferAccessor<TSnapshot>(GameWorld);

			// The code is a bit less complex here, since we assume that when we deserialize we do that for one client per instigator...
			var hadInitialData = Instigator.Storage.Has<InitialData>();
			if (!hadInitialData)
			{
				Array.Fill(baselineArray, default);

				parameters.Post.Schedule(setComponent, Instigator.Storage, default);
			}
			
			foreach (var ent in entities)
			{
				ref var baseline = ref baselineArray[ent.Id];

				var snapshot = default(TSnapshot);
				snapshot.Tick = parameters.Tick;
				snapshot.Deserialize(bitBuffer, baseline);

				if (ignoreSet[(int) ent.Id])
				{
					if (ForceToBufferIfEntityIgnoredSettings && AddToBufferSettings)
					{
						bufferAccessor[ent].Add(snapshot);
						if (bufferAccessor[ent].Count > 32)
							bufferAccessor[ent].RemoveAt(0);
					}
				}
				else
				{
					if (DirectComponentSettings)
						snapshot.ToComponent(ref dataAccessor[ent]);
					if (AddToBufferSettings)
					{
						bufferAccessor[ent].Add(snapshot);
						if (bufferAccessor[ent].Count > 32)
							bufferAccessor[ent].RemoveAt(0);
					}
				}

				baseline = snapshot;
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

		public struct InitialData
		{
		}
	}
}
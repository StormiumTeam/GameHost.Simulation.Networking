using System;
using System.Collections.Generic;
using Collections.Pooled;
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
	public abstract class DeltaBufferSerializerBase<TSnapshot, TComponent> : DeltaBufferSerializerBase<TSnapshot, TComponent, EmptySnapshotSetup>
		where TSnapshot : struct, ISnapshotData, IReadWriteSnapshotData<TSnapshot>, ISnapshotSyncWithComponent<TComponent>
		where TComponent : struct, IComponentBuffer
	{
		protected DeltaBufferSerializerBase([NotNull] ISnapshotInstigator instigator, [NotNull] Context ctx) : base(instigator, ctx)
		{
		}
	}
		
	public abstract class DeltaBufferSerializerBase<TSnapshot, TComponent, TSetup> : SerializerBase
		where TSnapshot : struct, ISnapshotData, IReadWriteSnapshotData<TSnapshot, TSetup>, ISnapshotSyncWithComponent<TComponent, TSetup>
		where TComponent : struct, IComponentBuffer
		where TSetup : struct, ISnapshotSetupData
	{
		private static readonly Action<Entity> setComponent = c => c.Set<InitialData>();

		#region Settings

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

		public DeltaBufferSerializerBase(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
		{
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

			var                     hadInitialData = group.Storage.Has<InitialData>();
			PooledList<TSnapshot>[] writeArray, readArray;

			ref var __readArray = ref instigatorDataMap[Instigator].BaselineArray;
			var     prevLength  = __readArray.Length;
			GetColumn(ref __readArray, entities);
			for (var i = prevLength; i < __readArray.Length; i++)
				__readArray[i] = new PooledList<TSnapshot>(ClearMode.Never);

			if (!hadInitialData)
			{
				writeArray = new PooledList<TSnapshot>[__readArray.Length];
				for (var i = 0; i < writeArray.Length; i++)
					writeArray[i] = new PooledList<TSnapshot>(ClearMode.Never);

				readArray = writeArray;
				parameters.Post.Schedule(setComponent, group.Storage, default);
			}
			else
			{
				writeArray = readArray = __readArray;
			}

			using var temporaryBuffer = new PooledList<TSnapshot>(ClearMode.Never);

			var accessor = new ComponentBufferAccessor<TComponent>(GameWorld);
			for (var ent = 0; ent < entities.Length; ent++)
			{
				var self   = entities[ent];
				var buffer = accessor[self];

				/*var prevReadLength = readArray[ent].Count;
				if (buffer.Count > readArray[ent].Count)
					readArray[ent].AddSpan(buffer.Count - readArray[ent].Count).Clear();
				else if (buffer.Count < readArray[ent].Count)
					readArray[ent].RemoveRange(buffer.Count, readArray[ent].Count - buffer.Count);

				temporaryBuffer.Clear();
				temporaryBuffer.AddSpan(buffer.Count);

				for (var i = 0; i < buffer.Count; i++)
				{
					var elem     = buffer[i];
					var snapshot = default(TSnapshot);
					snapshot.FromComponent(elem, setup);
					temporaryBuffer[i] = snapshot;
				}

				if (MemoryMarshal.AsBytes(readArray[ent].Span).SequenceEqual(MemoryMarshal.AsBytes(temporaryBuffer.Span))
				    && prevReadLength == buffer.Count)
				{
					bitBuffer.AddBool(false);
					continue;
				}*/

				bitBuffer.AddBool(true);
				bitBuffer.AddUIntD4Delta((uint) buffer.Count, (uint) default);
				for (var i = 0; i < buffer.Count; i++)
				{
					//var data = temporaryBuffer[i];
					//data.Serialize(bitBuffer, readArray[ent][i], setup);
					var snapshot = new TSnapshot();
					snapshot.FromComponent(buffer[i], setup);
					snapshot.Serialize(bitBuffer, default, setup);
					//writeArray[ent][i] = data;
				}
			}

			__readArray = writeArray;
		}

		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISnapshotSerializerSystem.RefData refData)
		{
			setup.Begin(false);
			
			ref var baselineArray = ref instigatorDataMap[Instigator].BaselineArray;

			var prevLength = baselineArray.Length;
			GetColumn(ref baselineArray, refData.Self);
			for (var i = prevLength; i < baselineArray.Length; i++)
				baselineArray[i] = new PooledList<TSnapshot>(ClearMode.Never);
			
			var bufferAccessor = new ComponentBufferAccessor<TComponent>(GameWorld);

			// The code is a bit less complex here, since we assume that when we deserialize we do that for one client per instigator...
			var hadInitialData = Instigator.Storage.Has<InitialData>();
			if (!hadInitialData)
			{
				foreach (var array in baselineArray)
					array.Clear();
				parameters.Post.Schedule(setComponent, Instigator.Storage, default);
			}

			for (var ent = 0; ent < refData.Self.Length; ent++)
			{
				var self = refData.Self[ent];
				if (!bitBuffer.ReadBool())
					continue;

				ref var baseline = ref baselineArray[ent];

				//var newLength = (int) bitBuffer.ReadUIntD4Delta((uint) baseline.Count);
				var newLength = (int) bitBuffer.ReadUIntD4Delta((uint) default);
				/*if (newLength > baseline.Count)
				{
					baseline.AddSpan(newLength - baseline.Count)
					        .Clear(); // make sure that the added span is zeroed
				}
				else if (newLength < baseline.Count)
					baseline.RemoveRange(newLength, baseline.Count - newLength);*/
				
				baseline.Clear();
				baseline.AddSpan(newLength);

				for (var i = 0; i < newLength; i++)
				{
					var inner = baseline.Span[i];
					//inner.Deserialize(bitBuffer, baseline[i], setup);
					inner.Deserialize(bitBuffer, default, setup);
					baseline[i] = inner;
				}
				
				// If we have been requested to add data to ignored entities, and those entities aren't null, do it.
				// The entity can be null (aka zero) if it doesn't exist. (This can happen if it got destroyed on our side, but not on the sender side)
				if (self.Id == 0)
					continue;

				var buffer = bufferAccessor[self];
				if (refData.IgnoredSet[(int) refData.Snapshot[ent].Id])
				{
					if (ForceToBufferIfEntityIgnoredSettings)
					{
						buffer.Clear();
						foreach (var data in baseline)
						{
							var component = default(TComponent);
							data.ToComponent(ref component, setup);
							buffer.Add(component);
						}
					}
				}
				else
				{
					buffer.Clear();
					foreach (var data in baseline)
					{
						var component = default(TComponent);
						data.ToComponent(ref component, setup);
						buffer.Add(component);
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
			public PooledList<TSnapshot>[] BaselineArray = Array.Empty<PooledList<TSnapshot>>();
		}

		public struct InitialData
		{
		}
	}
}
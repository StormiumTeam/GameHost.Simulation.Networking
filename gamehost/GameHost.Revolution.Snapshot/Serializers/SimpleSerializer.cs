using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Collections.Pooled;
using Cysharp.Threading.Tasks;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Serializers
{
	/// <summary>
	///     Represent a simple serializer that you can extend.
	/// </summary>
	public abstract class SerializerBase : AppObject, ISerializer
	{
		private readonly PooledList<byte>                  bytePool;
		private readonly Dictionary<MergeGroup, BitBuffer> dataPerGroup         = new();
		private readonly BitBuffer                         deserializeBitBuffer = new();

		private readonly Dictionary<MergeGroup, (SerializationParameters parameters, MergeGroup group, PooledList<GameEntityHandle> handles)> dict = new();

		private readonly PooledList<UniTask> tasks = new();

		private (DeserializationParameters p, PooledList<byte>? data, PooledList<GameEntityHandle>? handles, PooledList<GameEntityHandle>? snapshot, PooledList<bool>? valids) deserializeArgs;

		/// <summary>
		///     GameWorld reference
		/// </summary>
		protected GameWorld GameWorld;

		public SerializerBase(ISnapshotInstigator instigator, Context context) : base(context)
		{
			DependencyResolver.Add(() => ref GameWorld);
			DependencyResolver.OnComplete(OnDependenciesResolved);

			Instigator = instigator;
			bytePool   = new PooledList<byte>();
		}

		/// <summary>
		///     Serialize immediately without doing it on another thread.
		/// </summary>
		public virtual bool SynchronousSerialize => true; // temporary

		/// <summary>
		///     Deserialize immediately without doing it on another thread.
		/// </summary>
		public virtual bool SynchronousDeserialize => true; // temporary


		public ISnapshotInstigator Instigator { get; set; }

		public ISerializerArchetype     SerializerArchetype { get; private set; }
		public IAuthorityArchetype?     AuthorityArchetype  { get; private set; }
		public SnapshotSerializerSystem System              { get; set; }

		public abstract void UpdateMergeGroup(ReadOnlySpan<Entity> clients, MergeGroupCollection collection);
		
		public virtual Span<UniTask> PrepareSerializeTask(SerializationParameters parameters, MergeGroupCollection groupCollection, ReadOnlySpan<GameEntityHandle> entities)
		{
			PrepareGlobal();
			if (SynchronousSerialize)
			{
				foreach (var group in groupCollection)
				{
					if (group.ClientSpan.IsEmpty)
						continue;

					if (!dict.TryGetValue(group, out var tuple))
					{
						dict[group]         = tuple = (default, default!, new PooledList<GameEntityHandle>());
						dataPerGroup[group] = new BitBuffer();
					}

					tuple.parameters = parameters;
					tuple.group      = group;
					tuple.handles.Clear();
					tuple.handles.AddRange(entities);

					dict[group] = tuple;

					__serialize(group);
				}

				return Span<UniTask>.Empty;
			}

			tasks.Clear();
			foreach (var group in groupCollection)
			{
				if (group.ClientSpan.IsEmpty)
					continue;

				if (!dict.TryGetValue(group, out var tuple))
				{
					dict[group]         = tuple = (default, default!, new PooledList<GameEntityHandle>());
					dataPerGroup[group] = new BitBuffer();
				}

				tuple.parameters = parameters;
				tuple.group      = group;
				tuple.handles.Clear();
				tuple.handles.AddRange(entities);

				dict[group] = tuple;

				tasks.Add(UniTask.Run(__serialize, group));
			}

			return tasks.Span;
		}

		public Span<byte> FinalizeSerialize(MergeGroup group)
		{
			var bitBuffer = dataPerGroup[group];

			bytePool.Clear();
			var span = bytePool.AddSpan(bitBuffer.Length);
			bitBuffer.ToSpan(span);
			//Console.WriteLine($"WRITE {string.Join(',', span.ToArray())}");

			return span;
		}

		public virtual UniTask PrepareDeserializeTask(DeserializationParameters parameters, Span<byte> data, ISerializer.RefData refData)
		{
			PrepareGlobal();

			deserializeArgs.p        =   parameters;
			deserializeArgs.data     ??= new PooledList<byte>();
			deserializeArgs.handles  ??= new PooledList<GameEntityHandle>();
			deserializeArgs.snapshot ??= new PooledList<GameEntityHandle>();
			deserializeArgs.valids   ??= new PooledList<bool>();
			deserializeArgs.data.Clear();
			deserializeArgs.data.AddRange(data);
			deserializeArgs.handles.Clear();
			deserializeArgs.handles.AddRange(refData.Self);
			deserializeArgs.snapshot.Clear();
			deserializeArgs.snapshot.AddRange(refData.Snapshot);
			deserializeArgs.valids.Clear();
			deserializeArgs.valids.AddRange(refData.IgnoredSet);

			if (SynchronousDeserialize)
			{
				__deserialize();
				return UniTask.CompletedTask;
			}

			return UniTask.Run(__deserialize);
		}

		public abstract void OnReset(ISnapshotInstigator instigator);

		private void __serialize(object? state)
		{
			var (parameters, group, handles) = dict[(MergeGroup) state!];
			Serialize(parameters, group, handles.Span);
		}

		public void Serialize(SerializationParameters parameters, MergeGroup group, ReadOnlySpan<GameEntityHandle> entities)
		{
			lock (Synchronization)
			{
				var bitBuffer = dataPerGroup[group];
				bitBuffer.Clear();

				try
				{
					OnSerialize(bitBuffer, parameters, group, entities);
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException($"(Serializing) <{Thread.CurrentThread.Name}> {(this as ISerializer).Identifier} had an exception", ex);
				}
			}
		}

		protected virtual void PrepareGlobal()
		{
		}

		private void __deserialize()
		{
			// these variables shouldn't be null when this is called
			Deserialize(deserializeArgs.p, deserializeArgs.data!.Span, new ISerializer.RefData
			{
				Snapshot   = deserializeArgs.snapshot!.Span,
				Self       = deserializeArgs.handles!.Span,
				IgnoredSet = deserializeArgs.valids!.Span
			});
		}

		public void Deserialize(DeserializationParameters parameters, Span<byte> data, ISerializer.RefData refData) 
		{
			lock (Synchronization)
			{
				deserializeBitBuffer.Clear();
				deserializeBitBuffer.AddSpan(data);
				
				//Console.WriteLine($"READ {string.Join(',', data.ToArray())}");

				try
				{
					OnDeserialize(deserializeBitBuffer, parameters, refData);
				}
				catch (Exception ex)
				{
					throw new InvalidOperationException($"(Deserializing) <{Thread.CurrentThread.Name}> {(this as ISerializer).Identifier} had an exception", ex);
				}

				if (deserializeBitBuffer.readPosition > deserializeBitBuffer.nextPosition)
					throw new InvalidOperationException($"{(this as ISerializer).Identifier} has read too much data! ({deserializeBitBuffer.readPosition}b read, {deserializeBitBuffer.nextPosition}b written)");
			}
		}

		protected virtual void OnDependenciesResolved(IEnumerable<object> deps)
		{
			SerializerArchetype = GetSerializerArchetype();
			AuthorityArchetype  = GetAuthorityArchetype();
		}

		/// <summary>
		///     Get a <see cref="ISerializerArchetype" /> object which will manage entity validation and archetype deserialization.
		/// </summary>
		protected abstract ISerializerArchetype GetSerializerArchetype();

		/// <summary>
		///     Get a <see cref="IAuthorityArchetype" /> object which will manage entity authority on deserialization.
		/// </summary>
		protected virtual IAuthorityArchetype? GetAuthorityArchetype()
		{
			return null;
		}

		protected abstract void OnSerialize(BitBuffer   bitBuffer, SerializationParameters   parameters, MergeGroup          group, ReadOnlySpan<GameEntityHandle> entities);
		protected abstract void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISerializer.RefData refData);

		/// <summary>
		///     Automatically resize a column based on the maximum entity id.
		/// </summary>
		protected void GetColumn<T>(ref T[] array, ReadOnlySpan<GameEntityHandle> entities)
		{
			if (entities.Length == 0)
				return;

			if (array.Length <= entities[^1].Id)
				Array.Resize(ref array, (int) entities[^1].Id * 2 + 1);
		}
	}
}
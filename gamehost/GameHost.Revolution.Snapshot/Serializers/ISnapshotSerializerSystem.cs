using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DefaultEcs;
using GameHost.Core.Threading;
using GameHost.Native.Char;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using StormiumTeam.GameBase.Utility.Misc;

namespace GameHost.Revolution.Snapshot.Serializers
{
	public readonly struct SerializationParameters
	{
		public readonly uint       Tick;
		public readonly bool       HadEntityUpdate;
		public readonly IScheduler Post;

		public SerializationParameters(uint tick, bool hadEntityUpdate, IScheduler post)
		{
			Tick            = tick;
			HadEntityUpdate = hadEntityUpdate;
			Post            = post;
		}
	}

	public struct DeserializationParameters
	{
		public readonly uint       Tick;
		public readonly IScheduler Post;

		public DeserializationParameters(uint tick, IScheduler post)
		{
			Tick = tick;
			Post = post;
		}
	}

	public interface ISerializerArchetype
	{
		/// <summary>
		///     Components that are attached to an entity.
		/// </summary>
		Span<ComponentType> EntityComponents { get; }

		/// <summary>
		///     Check whether or not an archetype is valid for this serializer.
		/// </summary>
		/// <param name="archetype">The entity archetype</param>
		/// <returns>Whether or not it is valid</returns>
		bool IsArchetypeValid(in EntityArchetype archetype);

		/// <summary>
		///     Synchronize entity updates events from the deserializer.
		/// </summary>
		/// <param name="entities"></param>
		/// <param name="archetypes"></param>
		/// <param name="archetypeToSystems"></param>
		/// <remarks>
		///     Entities and Archetypes are part of the same 'array'.
		///     This mean that you get the archetype of an entity when you use the same index as you do for traversing the entity
		///     list.
		///
		///		ParentAuthority is a column.
		///		So you must index it via the entity Id:
		///			parentAuthority[entity.Id]
		/// </remarks>
		void OnDeserializerArchetypeUpdate(GameEntity self, SnapshotEntityArchetype requestedArchetype, Dictionary<uint, uint[]> archetypeToSystems, bool hasParentAuthority);
	}

	public interface IAuthorityArchetype
	{
		bool IsArchetypeValid(in EntityArchetype archetype);
		void TryKeepAuthority(GameEntity    entity, bool enable, HashSet<ComponentType> kept);
	}

	public interface IInstigatorSystem
	{
		/// <summary>
		///     Attached Instigator on current operations. This value can change.
		/// </summary>
		ISnapshotInstigator Instigator { get; set; }
		
		/// <summary>
		/// The serializer identifier
		/// </summary>
		string Identifier => GetIdentifier(GetType());

		public static string GetIdentifier(Type type)
		{
			var friendly = TypeExt.GetFriendlyName(type);
			
			//return $"{(char)CharBufferUtility.ComputeHashCode(type.Namespace)}{(char)CharBufferUtility.ComputeHashCode(friendly)}";
			return friendly;
		}

		/// <summary>
		///     The system state of this serializer
		/// </summary>
		InstigatorSystem System { get; set; }
	}

	/// <summary>
	///     Serializer object
	/// </summary>
	public interface ISnapshotSerializerSystem : IInstigatorSystem
	{
		public ref struct RefData
		{
			public ReadOnlySpan<GameEntityHandle> Snapshot;
			public ReadOnlySpan<GameEntityHandle> Self;
			public ReadOnlySpan<bool>             IgnoredSet;
		}

		/// <summary>
		///     The archetype manager.
		/// </summary>
		ISerializerArchetype? SerializerArchetype { get; }

		IAuthorityArchetype? AuthorityArchetype { get; }

		/// <summary>
		///     Update the groups of the clients, and maximize performance by having the least of groups.
		/// </summary>
		void UpdateMergeGroup(ReadOnlySpan<Entity> clients, MergeGroupCollection collection);

		/// <summary>
		///     Prepare the serialization
		/// </summary>
		/// <returns>Return a list of parallel tasks that must be finished</returns>
		Span<UniTask> PrepareSerializeTask(SerializationParameters parameters, MergeGroupCollection group, ReadOnlySpan<GameEntityHandle> entities);

		/// <summary>
		///     Finalize the serialization of a group of clients and get the data result.
		/// </summary>
		/// <returns>Data result of the serialization.</returns>
		/// <remarks>
		///     Make sure that the tasks are completed before calling this method.
		/// </remarks>
		Span<byte> FinalizeSerialize(MergeGroup group);

		/// <summary>
		///     Prepare the deserialization
		/// </summary>
		/// <returns>Return a task that can be done in parallel and must be terminated later.</returns>
		UniTask PrepareDeserializeTask(DeserializationParameters parameters, Span<byte> data, RefData entities);

		void OnReset(ISnapshotInstigator instigator);
	}
}
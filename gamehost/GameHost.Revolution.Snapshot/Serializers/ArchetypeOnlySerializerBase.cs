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
	public abstract class ArchetypeOnlySerializerBase<TComponent> : SerializerBase
		where TComponent : struct, IEntityComponent
	{
		public ArchetypeOnlySerializerBase(ISnapshotInstigator instigator, Context ctx) : base(instigator, ctx)
		{
		}

		protected override ISerializerArchetype GetSerializerArchetype()
		{
			if (System.Id == 0)
				throw new InvalidOperationException("This serializer should have been set with a system.");

			var component = GameWorld.AsComponentType<TComponent>();
			return new SimpleSerializerArchetype(this, GameWorld, component, new[] {component}, Array.Empty<ComponentType>());
		}

		public override void UpdateMergeGroup(ReadOnlySpan<Entity> clients, MergeGroupCollection collection)
		{
		}

		protected override void OnSerialize(BitBuffer bitBuffer, SerializationParameters parameters, MergeGroup group, ReadOnlySpan<GameEntityHandle> entities)
		{
		}

		protected override void OnDeserialize(BitBuffer bitBuffer, DeserializationParameters parameters, ISerializer.RefData refData)
		{
		}
	}
}
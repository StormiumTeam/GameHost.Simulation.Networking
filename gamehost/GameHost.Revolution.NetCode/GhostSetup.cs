using System;
using System.Runtime.CompilerServices;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;

namespace GameHost.Revolution.NetCode
{
	public struct Ghost
	{
		public bool       FromLocal;
		public GameEntity Source;

		public override string ToString()
		{
			return $"Ghost(Local={FromLocal}, {Source})";
		}
	}

	public static class GhostBitBufferUtility
	{
		public static BitBuffer AddGhostDelta(this BitBuffer bitBuffer, Ghost ghost, Ghost baseline)
		{
			return bitBuffer.AddBool(ghost.FromLocal)
			                .AddUIntD4Delta(ghost.Source.Id, baseline.Source.Id)
			                .AddUIntD4Delta(ghost.Source.Version, baseline.Source.Version);
		}

		public static Ghost ReadGhostDelta(this BitBuffer bitBuffer, Ghost baseline)
		{
			return new()
			{
				FromLocal = bitBuffer.ReadBool(),
				Source    = new GameEntity(bitBuffer.ReadUIntD4Delta(baseline.Source.Id), bitBuffer.ReadUIntD4Delta(baseline.Source.Version))
			};
		}
	}

	public struct GhostSetup : ISnapshotSetupData
	{
		private SerializerBase serializer;
		private GameWorld      gameWorld;

		public void Create(SerializerBase serializer)
		{
			this.serializer = serializer;
			this.gameWorld  = (GameWorld) serializer.DependencyResolver.DefaultStrategy.ResolveNow(typeof(GameWorld));
		}

		public ISnapshotState snapshotState;

		private bool isSerialization;
		public void Begin(bool isSerialization)
		{
			snapshotState = serializer.Instigator.State;

			this.isSerialization = isSerialization;
		}

		public readonly Ghost ToGhost(GameEntity local)
		{
			if (!isSerialization)
				throw new InvalidOperationException("Can't be called while deserializing");
			
			var self = snapshotState.LocalToSelf(local);
			// Perhaps this is an entity from the broadcaster and we didn't networked it.
			// This method is only used in serialization
			if (local != default && self == default
			                     && gameWorld.HasComponent<SnapshotEntity>(local.Handle))
			{
				// Ok, so, this is an entity that we didn't networked to the broadcaster, but that the broadcaster sent to us.
				// Just get its ID then
				return new Ghost {FromLocal = true, Source = gameWorld.GetComponentData<SnapshotEntity>(local.Handle).Source};
			}

			return new Ghost {FromLocal = false, Source = self};
		}

		public readonly GameEntity FromGhost(Ghost ghost)
		{
			if (isSerialization)
				throw new InvalidOperationException("can't be called while serializing");

			return ghost.FromLocal
				? ghost.Source
				: snapshotState.LocalToSelf(ghost.Source);
		}

		public void Clean()
		{
			snapshotState = default;
		}
	}
}
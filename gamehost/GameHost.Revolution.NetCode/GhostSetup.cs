using System;
using System.Runtime.CompilerServices;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;

namespace GameHost.Revolution.NetCode
{
	public struct GhostSetup : ISnapshotSetupData
	{
		private SerializerBase serializer;

		public void Create(SerializerBase serializer)
		{
			this.serializer = serializer;
		}

		private ISnapshotState snapshotState;

		public void Begin(bool isSerialization)
		{
			snapshotState = serializer.Instigator.State;
		}

		public GameEntity this[GameEntity local]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get => snapshotState.LocalToSelf(local);
		}

		public void Clean()
		{
			snapshotState = default;
		}
	}
}
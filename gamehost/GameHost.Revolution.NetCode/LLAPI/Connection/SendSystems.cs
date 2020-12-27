using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using GameHost.Simulation.Application;
using GameHost.Worlds.Components;
using K4os.Compression.LZ4;
using RevolutionSnapshot.Core.Buffers;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
	[RestrictToApplication(typeof(SimulationApplication))]
	public class SendSystems : AppSystemWithFeature<ServerFeature>
	{
		private SerializerCollection serializerCollection;

		public SendSystems(WorldCollection collection) : base(collection)
		{
			DependencyResolver.Add(() => ref serializerCollection);
		}

		protected override void OnDependenciesResolved(IEnumerable<object> dependencies)
		{
			base.OnDependenciesResolved(dependencies);
			
			serializerCollection.OnCollectionUpdate += onCollectionUpdate;
		}

		private void onCollectionUpdate(IReadOnlyDictionary<string, (Type type, Func<ISnapshotInstigator, IInstigatorSystem> createFunc)> obj)
		{
			requireUpdate = true;
			dictionary    = obj;
		}

		private bool                                                                                               requireUpdate = false;
		private IReadOnlyDictionary<string, (Type type, Func<ISnapshotInstigator, IInstigatorSystem> createFunc)>? dictionary;

		private struct HasInitialData
		{
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			if (dictionary == null)
				return;

			foreach (var (entity, feature) in Features)
			{
				if (!requireUpdate && entity.Has<HasInitialData>())
					continue;

				updateSerializers(entity.Get<BroadcastInstigator>());
				doFeature(entity, feature);
				
				entity.Set<HasInitialData>();
			}

			requireUpdate = false;
		}

		private void updateSerializers(BroadcastInstigator broadcast)
		{
			Debug.Assert(dictionary != null, "dictionary != null");

			foreach (var (name, (type, createFunc)) in dictionary)
			{
				if (broadcast.Serializers.Any(kvp => kvp.Value.Identifier == name))
					continue;

				broadcast.AddSerializer(createFunc(broadcast));
			}
			
			// Clear all archetypes
			Console.WriteLine($"    CLEAR! ! ! ! ! !");
			(broadcast.State as BroadcastInstigator.SnapshotState).ClearAllAssignedArchetype();

			// TODO: Check for removed serializers
			// (right now it's not really possible to remove a serializer in SerializerCollection, so it's not really yet necessary)
		}

		private PooledList<TransportConnection> tempConnectionList = new();
		private void doFeature(Entity featureEnt, ServerFeature feature)
		{
			tempConnectionList.Clear();
			Span<TransportConnection> connections = tempConnectionList.AddSpan(feature.Driver.GetConnectionCount());
			feature.Driver.GetConnections(connections);

			foreach (var con in connections)
				SendToClient(featureEnt, feature, con);
		}

		public void SendToClient(Entity featureEnt, ServerFeature feature, TransportConnection connection)
		{
			Debug.Assert(connection.IsCreated, "connection.IsCreated");

			if (!featureEnt.TryGet(out BroadcastInstigator broadcaster))
				throw new InvalidOperationException("where banana?");

			using var dataBuffer = new DataBufferWriter(0);
			dataBuffer.WriteValue(NetCodeMessageType.SendSnapshotSystems);

			using var compressBuffer = new DataBufferWriter(0);
			compressBuffer.WriteInt(broadcaster.Serializers.Count);
			foreach (var (id, serializer) in broadcaster.Serializers)
			{
				compressBuffer.WriteValue(id);
				compressBuffer.WriteStaticString(serializer.Identifier);
			}
			
			dataBuffer.WriteCompressed(compressBuffer.Span, LZ4Level.L12_MAX);

			feature.Driver.Send(feature.ReliableChannel, connection, dataBuffer.Span);
		}
	}
}
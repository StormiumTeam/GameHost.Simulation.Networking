using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Core.Features.Systems;
using GameHost.Core.IO;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems;
using GameHost.Revolution.Snapshot.Systems.Instigators;
using RevolutionSnapshot.Core.Buffers;

namespace GameHost.Revolution.NetCode.LLAPI.Systems
{
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

		private void onCollectionUpdate(IReadOnlyDictionary<string, (Type type, Func<ISnapshotInstigator, ISerializer> createFunc)> obj)
		{
			requireUpdate = true;
			dictionary    = obj;
		}

		private bool                                                                                         requireUpdate = false;
		private IReadOnlyDictionary<string, (Type type, Func<ISnapshotInstigator, ISerializer> createFunc)>? dictionary;

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

			// TODO: Check for removed serializers
			// (right now it's not really possible to remove a serializer in SerializerCollection, so it's not really yet necessary)
		}

		private void doFeature(Entity featureEnt, ServerFeature feature)
		{
			Span<TransportConnection> connections = stackalloc TransportConnection[feature.Driver.GetConnectionCount()];
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
			dataBuffer.WriteInt(broadcaster.Serializers.Count);
			foreach (var (id, serializer) in broadcaster.Serializers)
			{
				dataBuffer.WriteValue(id);
				dataBuffer.WriteStaticString(serializer.Identifier);
			}

			feature.Driver.Send(feature.ReliableChannel, connection, dataBuffer.Span);
		}
	}
}
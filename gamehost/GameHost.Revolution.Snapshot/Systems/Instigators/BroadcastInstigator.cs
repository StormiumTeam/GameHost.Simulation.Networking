using System;
using Collections.Pooled;
using DefaultEcs;
using GameHost.Core.Ecs;
using GameHost.Injection;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	/* Generally, servers broadcaster should have their GroupId at 0.
	 * Clients broadcaster should have the same GroupId as the one that is assigned when receiving data.
	 * But when you add the server as a client in a client broadcaster, you should put it with the GroupId at 0.
	 */
	public partial class BroadcastInstigator : AppObject, ISnapshotInstigator
	{
		private readonly PooledList<ClientSnapshotInstigator> clients;

		private readonly Serialization           serialization = new();
		private readonly PooledList<ISerializer> serializers;
		private          GameWorld               gameWorld;

		/// <summary>
		///     Constructor
		/// </summary>
		/// <param name="storage">
		///     The entity storage for this instigator (if you use a custom one, you will need to manually
		///     dispose it)
		/// </param>
		/// <param name="groupId">The ID of this instigator. If its a server, it should be 0.</param>
		/// <param name="context">Data Context</param>
		public BroadcastInstigator(Entity? storage, int groupId, Context context) : base(context)
		{
			AddDisposable(clients     = new PooledList<ClientSnapshotInstigator>());
			AddDisposable(serializers = new PooledList<ISerializer>());
			AddDisposable(Serializers = new PooledDictionary<uint, ISerializer>());

			State = new SnapshotState(groupId);

			if (storage != null)
			{
				Storage = storage.Value;
			}
			else
			{
				Storage = new ContextBindingStrategy(context, true).Resolve<World>()
				                                                   .CreateEntity();
				AddDisposable(Storage);
			}

			QueuedEntities = new EntityGroup();
			InstigatorId   = groupId;

			var queuedEntity = Storage.World.CreateEntity();
			queuedEntity.SetAsChildOf(Storage);

			DependencyResolver.Add(() => ref gameWorld);
		}

		public EntityGroup QueuedEntities { get; }

		public PooledDictionary<uint, ISerializer> Serializers { get; }


		public Entity         Storage { get; }
		public ISnapshotState State   { get; }

		public int InstigatorId { get; }

		public ClientSnapshotInstigator AddClient(int clientId)
		{
			if (clientId == InstigatorId)
				throw new InvalidOperationException("clientId shouldn't be GroupId");

			var client = new ClientSnapshotInstigator(Storage.World.CreateEntity(), clientId, gameWorld)
			{
				Serializers = Serializers
			};
			client.GetClientState(out _).Operation = ClientState.EOperation.RecreateFull;

			clients.Add(client);
			return client;
		}

		public void AddSerializer(ISerializer serializer, bool disposeOnThisDispose = true)
		{
			serializers.Add(serializer);

			serializer.System                 = new SnapshotSerializerSystem((uint) serializers.Count);
			Serializers[serializer.System.Id] = serializer;
		}

		public void Serialize(uint tick)
		{
			serialization.Execute(tick, this);
		}
	}
}
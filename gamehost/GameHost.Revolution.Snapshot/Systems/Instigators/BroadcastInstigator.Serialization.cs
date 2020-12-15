using System;
using System.Collections.Generic;
using System.Threading;
using Collections.Pooled;
using Cysharp.Threading.Tasks;
using DefaultEcs;
using GameHost.Core.Threading;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;
using GameHost.Simulation.TabEcs.HLAPI;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	public partial class BroadcastInstigator
	{
		public class Serialization
		{
			private readonly PooledList<uint> archetypeAddedList = new();
			private readonly PooledList<uint> archetypeList      = new();

			private readonly BitBuffer bitBuffer = new();

			private readonly Dictionary<SnapshotSerializerSystem, PooledList<GameEntityHandle>> entitiesPerSystem = new();

			private readonly PooledList<GameEntity>                                     entityList       = new();
			private readonly PooledList<GameEntity>                                     entityRemoveList = new();
			private readonly PooledList<GameEntity>                                     entityUpdateList = new();
			private readonly Dictionary<SnapshotSerializerSystem, MergeGroupCollection> groupsPerSystem  = new();
			private readonly IScheduler                                                 scheduler        = new Scheduler();
			private readonly PooledList<UniTask>                                        tasks            = new();

			/// <summary>
			///     Assigned by BroadcastInstigator
			/// </summary>
			public SnapshotState WriterState;

			public void Execute(uint tick, BroadcastInstigator broadcast)
			{
				var gameWorld   = broadcast.gameWorld;
				var entityBoard = gameWorld.Boards.Entity;
				
				WriterState      = (SnapshotState) broadcast.State;
				WriterState.Tick = tick;

				tasks.Clear();
				
				foreach (var client in broadcast.clients)
				{
					client.Prepare();
				}

				WriterState.Prepare((int) BoardContainerExt.GetBoard(entityBoard).MaxId);
				unchecked
				{
					foreach (var ent in broadcast.QueuedEntities)
					{
						if (entityBoard.VersionColumn[(int) ent.Id] != ent.Version)
							continue;

						WriterState.RegisterEntity(ent, true, true);

						// ------- Setup Archetype ------- //
						// Entities must have an archetype assigned.
						// Even if it's an empty archetype.
						var localArchetype = gameWorld.GetArchetype(ent.Handle);
						if (!WriterState.AreSameLocalArchetype(ent, localArchetype.Id)
						    || !WriterState.TryGetArchetypeFromEntity(ent, out var archetype))
						{
							using var systems          = new PooledList<uint>();
							using var authoritySystems = new PooledList<uint>();
							foreach (var (id, serializer) in broadcast.Serializers)
							{
								if (serializer.SerializerArchetype?.IsArchetypeValid(localArchetype) == true)
									systems.Add(id);
								if (serializer.AuthorityArchetype?.IsArchetypeValid(localArchetype) == true)
									authoritySystems.Add(id);
							}

							if (!WriterState.TryGetArchetypeWithSystems(systems.Span, out archetype))
								archetype = WriterState.CreateArchetype(systems.Span);
							if (!WriterState.TryGetArchetypeWithSystems(authoritySystems.Span, out var authorityArchetype))
								authorityArchetype = WriterState.CreateArchetype(authoritySystems.Span);

							WriterState.AssignSnapshotArchetype(ent, archetype, authorityArchetype, localArchetype.Id);
						}
					}
				}

				entityList.Clear();
				entityUpdateList.Clear();
				entityRemoveList.Clear();

				archetypeList.Clear();
				archetypeAddedList.Clear();
				WriterState.FinalizeRegister((entityList, entityUpdateList, entityRemoveList), (archetypeList, archetypeAddedList));

				broadcast.QueuedEntities.FullClear();

				// Entities must be in order for:
				// - Delta compression
				// - Index order on clients (or else everything will be messed up)
				// - More predictable (first entity is min ID, last entity is max ID)
				foreach (var entity in entityList)
				{
					var handle = entity.Handle;

					// If nobody assigned the SnapshotEntity component, this mean we created it (or that we can take possession of it)
					if (!gameWorld.HasComponent<SnapshotEntity>(handle))
					{
						gameWorld.AddComponent(handle, new SnapshotEntity(new GameEntity(handle.Id, entity.Version), broadcast.InstigatorId, broadcast.Storage));
					}
				}

				foreach (var serializer in broadcast.serializers)
				{
					var state = new SnapshotSerializerSystem(serializer.System.Id);

					if (!entitiesPerSystem.TryGetValue(state, out var list))
					{
						entitiesPerSystem[state] = new PooledList<GameEntityHandle>();
						groupsPerSystem[state]   = new MergeGroupCollection();
					}

					list?.Clear();
				}

				foreach (var entity in entityList)
				{
					uint netArchetype;
					WriterState.TryGetArchetypeFromEntity(entity, out netArchetype);

					var systems = WriterState.GetArchetypeSystems(netArchetype);
					foreach (var sys in systems) entitiesPerSystem[new SnapshotSerializerSystem(sys)].Add(entity.Handle);
				}

				// ---- ENTITY
				WriteEntities(broadcast, gameWorld);
				
				// Write Owned entities
				foreach (var client in broadcast.clients)
				{
					var clientData  = client.GetClientData();
					var clientState = (ClientSnapshotState) client.State;

					var owned = client.OwnedEntities;

					var prevLocalId  = 0u;
					var prevLocalVer = 0u;
					var prevCdArch   = 0u;
					var prevPerm     = 0u;

					var b = clientData.Length;
					clientData.AddUIntD4((uint) owned.Count);
					foreach (var data in owned.Span)
					{
						// The caller did not assign any archetype to this entity, so let's set it
						var componentDataArchetype = data.ComponentDataArchetype;
						if (componentDataArchetype == 0)
							componentDataArchetype = WriterState.GetAuthorityArchetypeOfEntity(data.Entity.Id);

						clientData.AddUIntD4Delta(data.Entity.Id, prevLocalId)
						          .AddUIntD4Delta(data.Entity.Version, prevLocalVer)
						          .AddUIntD4Delta(componentDataArchetype, prevCdArch)
						          .AddUIntD4Delta((uint) data.Permission, prevPerm);
						prevLocalId  = data.Entity.Id;
						prevLocalVer = data.Entity.Version;
						prevCdArch   = componentDataArchetype;
						prevPerm     = (uint) data.Permission;

						if (clientState.ownedArch.Length > prevLocalId)
							clientState.ownedArch[prevLocalId] = prevCdArch;
					}

					owned.Clear();
				}

				// ---- SYSTEMS
				PrepareSerializers(broadcast);

				foreach (var task in tasks)
				{
					var awaiter = task.GetAwaiter();
					while (!awaiter.IsCompleted)
					{
					}
				}

				foreach (var serializer in broadcast.serializers)
				{
					var groupCollection = groupsPerSystem[serializer.System];
					foreach (var group in groupCollection)
					{
						var data = serializer.FinalizeSerialize(group);
						
						foreach (var client in group.ClientSpan)
						{
							var clientData = client.Get<ClientData>();
							clientData.AddUIntD4(serializer.System.Id)
							          .AddUIntD4((uint) data.Length);
							clientData.AddSpan(data);
						}
					}
				}

				scheduler.Run();

				foreach (var client in broadcast.clients)
				{
					client.StopPrepare();

					var state = client.GetClientState(out _);
					state.Operation = ClientState.EOperation.None;
				}
			}

			private void WriteArchetypes(ReadOnlySpan<uint> archetypes, BroadcastInstigator instigator)
			{
				bitBuffer.AddUIntD4((uint) archetypes.Length);

				var previousArchetypeId = 0u;
				var previousSystemId    = 0u;
				foreach (var archetype in archetypes)
				{
					previousSystemId = 0;

					bitBuffer.AddUIntD4Delta(archetype, previousArchetypeId);
					var systems = instigator.State.GetArchetypeSystems(archetype);
					bitBuffer.AddUIntD4((uint) systems.Length);
					for (var i = 0; i != systems.Length; i++)
					{
						bitBuffer.AddUIntD4Delta(systems[i], previousSystemId);
						previousSystemId = systems[i];
					}

					previousArchetypeId = archetype;
				}
			}

			private bool WriteEntities(BroadcastInstigator broadcast, GameWorld gameWorld)
			{
				// We have two identifier for entities. The local and remote one.
				// The local one is made from the snapshot creator.
				// The remote one is from the one who originally created the entity.
				// In a simple authoritative Server, the remote and local identifiers will be the same.
				void writeEntitySpan(Span<GameEntity> entities)
				{
					var networkedEntityAccessor = new ComponentDataAccessor<SnapshotEntity>(gameWorld);

					var prevLocalId       = 0u;
					var prevLocalVersion  = 0u;
					var prevRemoteId      = 0u;
					var prevRemoteVersion = 0u;
					var prevArchetype     = 0u;
					var prevInstigator    = 0;
					
					bitBuffer.AddUIntD4((uint) entities.Length);
					foreach (var ent in entities)
					{
						var networked = networkedEntityAccessor[ent.Handle];
						var archetype = WriterState.GetArchetypeOfEntity(ent.Id);
						bitBuffer.AddUIntD4Delta(ent.Id, prevLocalId)
						         .AddUIntD4Delta(ent.Version, prevLocalVersion)
						         .AddUIntD4Delta(networked.Source.Id, prevRemoteId)
						         .AddUIntD4Delta(networked.Source.Version, prevRemoteVersion)
						         .AddUIntD4Delta(archetype, prevArchetype)
						         .AddIntDelta(networked.Instigator, prevInstigator);
						prevLocalId       = ent.Id;
						prevLocalVersion  = ent.Version;
						prevRemoteId      = networked.Source.Id;
						prevRemoteVersion = networked.Source.Version;
						prevArchetype     = archetype;
						prevInstigator    = networked.Instigator;

						Console.WriteLine($"Write ({prevLocalId}, {prevLocalVersion})");
					}
				}

				bitBuffer.Clear();

				var wantedGlobalData = broadcast.clients.Count;
				foreach (var client in broadcast.clients)
				{
					var data = client.GetClientData();
					data.Clear();
					data.AddUInt(WriterState.Tick);

					if (client.GetClientState(out _).Operation == ClientState.EOperation.RecreateFull)
					{
						wantedGlobalData--;

						bitBuffer.Clear();

						// The bool here indicate whether or not the client need to know that it's a remake of the list
						// In this case, it is
						bitBuffer.AddBool(true);

						// ----- ARCHETYPE PART ----- //
						WriteArchetypes(archetypeList.Span, broadcast);

						// ----- ENTITY PART ----- //
						writeEntitySpan(entityList.Span);

						data.CopyFrom(bitBuffer);
					}
				}

				// If atleast one user can have access to global data, create it
				if (wantedGlobalData != 0)
				{
					bitBuffer.Clear();

					// The bool here indicate whether or not the client need to know that it's a remake of the list
					// In this case, it's not
					bitBuffer.AddBool(false);

					// ----- ARCHETYPE PART ----- //
					WriteArchetypes(archetypeAddedList.Span, broadcast);

					// ----- ENTITY PART ----- //
					uint prevId      = 0;
					uint prevVersion = 0;

					// The bool here indicate whether or not the client need to know that it's a remake of the list
					// In this case, it's not
					writeEntitySpan(entityUpdateList.Span);

					bitBuffer.AddUIntD4((uint) entityRemoveList.Count);
					foreach (var entity in entityRemoveList)
					{
						bitBuffer.AddUIntD4Delta(entity.Id, prevId)
						         .AddUIntD4Delta(entity.Version, prevVersion);
						prevId      = entity.Id;
						prevVersion = entity.Version;
					}

					foreach (var client in broadcast.clients)
					{
						if (client.GetClientState(out _).Operation == ClientState.EOperation.RecreateFull)
							continue;

						var data = client.GetClientData();
						data.CopyFrom(bitBuffer);
					}
				}

				return wantedGlobalData != 0;
			}

			private void PrepareSerializers(BroadcastInstigator broadcast)
			{
				Span<Entity> clients = stackalloc Entity[broadcast.clients.Count];
				for (var i = 0; i != clients.Length; i++)
					clients[i] = broadcast.clients[i].Storage;

				var parameters = new SerializationParameters(0, scheduler);
				foreach (var serializer in broadcast.serializers)
				{
					var groups = groupsPerSystem[serializer.System];

					serializer.Instigator = broadcast;
					serializer.UpdateMergeGroup(clients, groups);
					tasks.AddRange(serializer.PrepareSerializeTask(parameters, groups, entitiesPerSystem[serializer.System].Span));
				}
			}
		}
	}
}
using System;
using System.Collections.Generic;
using System.Linq;
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

			private readonly Dictionary<InstigatorSystem, PooledList<GameEntityHandle>> entitiesPerSystem = new();

			private readonly PooledList<GameEntity>                                     entityList       = new();
			private readonly PooledList<GameEntity>                                     entityRemoveList = new();
			private readonly PooledList<GameEntity>                                     entityUpdateList = new();
			internal readonly Dictionary<InstigatorSystem, MergeGroupCollection> groupsPerSystem  = new();
			private readonly IScheduler                                                 scheduler        = new Scheduler();
			private readonly PooledList<UniTask>                                        tasks            = new();

			/// <summary>
			///     Assigned by BroadcastInstigator
			/// </summary>
			public SnapshotState WriterState;

			public void Execute(uint tick, uint baseline, BroadcastInstigator broadcast)
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
							foreach (var (id, obj) in broadcast.Serializers)
							{
								if (obj is not ISnapshotSerializerSystem serializer)
									continue;
								
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
						if (gameWorld.HasComponent<SnapshotEntity.ForcedInstigatorId>(handle))
						{
							gameWorld.AddComponent(handle, new SnapshotEntity(
								new GameEntity(handle.Id, entity.Version),
								gameWorld.GetComponentData<SnapshotEntity.ForcedInstigatorId>(handle).Value,
								broadcast.Storage
							));
						}
						else
						{
							gameWorld.AddComponent(handle, new SnapshotEntity(
								new GameEntity(handle.Id, entity.Version),
								broadcast.InstigatorId,
								broadcast.Storage
							));
						}

						gameWorld.AddComponent(handle, new SnapshotEntity.CreatedByThisWorld());
					}
				}

				foreach (var serializer in broadcast.snapshotSerializers)
				{
					var state = new InstigatorSystem(serializer.System.Id);

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
					foreach (var sys in systems) entitiesPerSystem[new InstigatorSystem(sys)].Add(entity.Handle);
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

						if (clientState.selfToSnapshot.TryGetValue(data.Entity, out var ghost))
						{
							ref var ghostRef = ref clientState.GetRefGhost(ghost.Id);
							ghostRef.OwnedArchetype = prevCdArch;
						}
					}

					owned.Clear();
				}

				// ---- SYSTEMS
				PrepareSerializers(broadcast, baseline);

				foreach (var task in tasks)
				{
					var awaiter = task.GetAwaiter();
					while (!awaiter.IsCompleted)
					{
					}
				}

				var orderSize = new List<(int size, string name)>();
				var totalSize = 0;
				var lastReportSize = 0;
				foreach (var serializer in broadcast.snapshotSerializers)
				{
					var groupCollection = groupsPerSystem[serializer.System];
					foreach (var group in groupCollection)
					{
						var data = serializer.FinalizeSerialize(group);
						if (data.IsEmpty)
						{
							continue;
						}

						foreach (var client in group.ClientSpan)
						{
							var clientData = client.Get<ClientData>();
							if (!clientData.PreviousSizePerSystem.TryGetValue(serializer.System.Id, out var previousSize))
								clientData.PreviousSizePerSystem[serializer.System.Id] = 0;
							
							clientData.AddUIntD4Delta(serializer.System.Id, clientData.LastSystemId)
							          .AddUIntD4Delta((uint) data.Length, previousSize);
							clientData.AddSpan(data);

							clientData.LastSystemId                                = serializer.System.Id;
							clientData.PreviousSizePerSystem[serializer.System.Id] = (uint) data.Length;
							
							orderSize.Add((data.Length, serializer.GetType().FullName));
							totalSize      += data.Length;
							lastReportSize =  clientData.Length;
						}
					}
				}

				if (broadcast.InstigatorId == 0)
				{
					Console.WriteLine($"Ordered Size average={{{totalSize}}} lastReport={{{lastReportSize}}}");
					Console.WriteLine($"{string.Join('\n', orderSize.OrderByDescending(t => t.size).Select(t => $"\t{t.size}B - {t.name}"))}");
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
					}
				}

				bitBuffer.Clear();

				var wantedDeltaData = broadcast.clients.Count;
				foreach (var client in broadcast.clients)
				{
					var data = client.GetClientData();
					data.Clear();
					
					data.AddUInt(WriterState.Tick);
					data.AddLong(69420); // guard
					data.AddUInt(entityList.LastOrDefault().Id);

					if (client.GetClientState(out _).Operation == ClientState.EOperation.RecreateFull)
					{
						wantedDeltaData--;

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

				// If atleast one user can have access to delta data, create it
				if (wantedDeltaData != 0)
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

				return wantedDeltaData != 0;
			}

			private PooledList<Entity> tempClientList = new();
			private void PrepareSerializers(BroadcastInstigator broadcast, uint baseline)
			{
				tempClientList.Clear();
				var clients = tempClientList.AddSpan(broadcast.clients.Count);
				for (var i = 0; i != clients.Length; i++)
					clients[i] = broadcast.clients[i].Storage;

				var parameters = new SerializationParameters(broadcast.State.Tick, baseline, entityUpdateList.Count > 0, scheduler);
				foreach (var serializer in broadcast.snapshotSerializers)
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
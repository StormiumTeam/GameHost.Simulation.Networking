using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Collections.Pooled;
using Cysharp.Threading.Tasks;
using GameHost.Core.Threading;
using GameHost.Revolution.Snapshot.Serializers;
using GameHost.Revolution.Snapshot.Systems.Components;
using GameHost.Revolution.Snapshot.Utilities;
using GameHost.Simulation.TabEcs;

namespace GameHost.Revolution.Snapshot.Systems.Instigators
{
	public partial class ClientSnapshotInstigator
	{
		private class Deserialization
		{
			private readonly PooledList<SnapshotEntityArchetype> archetypeUpdateList = new();
			private readonly BitBuffer                           bitBuffer           = new();
			private readonly PooledList<byte>                    bytePool            = new();
			private readonly PooledList<GameEntity>              entityUpdateList    = new();
			
			private readonly PooledList<GameEntity> ownedUpdateList = new();

			private readonly PooledDictionary<uint, (PooledList<GameEntityHandle> snapshot, PooledList<GameEntityHandle> self)> systemToEntities = new();
			private readonly PooledDictionary<uint, PooledList<bool>>                                      systemToIgnored  = new();

			private readonly PooledList<UniTask> tasks = new();

			private readonly PooledList<uint> tempSystems = new();

			private readonly PooledList<GameEntity> toDestroy = new();

			private readonly HashSet<ComponentType> keptComponents = new();
			
			private ClientSnapshotInstigator client;
			private ClientSnapshotState      readState;

			public void Deserialize(ClientSnapshotInstigator c, Span<byte> data)
			{
				client    = c;
				readState = (ClientSnapshotState) client.State;
				readState.Prepare();

				var parentState = (BroadcastInstigator.SnapshotState) client.parent.State;

				bitBuffer.readPosition = 0;
				bitBuffer.nextPosition = 0;
				bitBuffer.AddSpan(data);

				tasks.Clear();
				toDestroy.Clear();
				entityUpdateList.Clear();
				archetypeUpdateList.Clear();
				ownedUpdateList.Clear();
				
				readState.Tick = bitBuffer.ReadUInt();
				
				var isRemake = bitBuffer.ReadBool();
				if (isRemake)
				{
					ReadArchetypes();
					ReadEntityUpdateOrAdd();
				}
				else
				{
					ReadArchetypes();
					ReadEntityUpdateOrAdd();
					ReadRemovedEntity();
				}

				// Read owned entities
				{
					var prevLocalId  = 0u;
					var prevLocalVer = 0u;
					var prevCdArch   = 0u;
					var prevPerm     = 0u;

					var ownedCount = bitBuffer.ReadUIntD4();
					while (ownedCount-- > 0)
					{
						prevLocalId  = bitBuffer.ReadUIntD4Delta(prevLocalId);
						prevLocalVer = bitBuffer.ReadUIntD4Delta(prevLocalVer);
						prevCdArch   = bitBuffer.ReadUIntD4Delta(prevCdArch);
						prevPerm     = bitBuffer.ReadUIntD4Delta(prevPerm);

						var self = readState.GetSelfEntity(new GameEntity(prevLocalId, prevLocalVer)).Id;
						if (self == 0)
							continue;

						if (readState.parentOwned[self])
						{
							throw new InvalidOperationException($"Ownership: Applying one an entity that the parent has created.\nThis is not acceptable.");
						}

						readState.owned[self]     = true;
						if (readState.ownedArch[self] != prevCdArch)
						{
							// Remove previous authorities
							var prevArch = readState.ownedArch[self];
							
							readState.ownedArch[self] = prevCdArch;
							ownedUpdateList.Add(client.gameWorld.Safe(new GameEntityHandle(self)));

							var ownedArchetype       = readState.ownedArch[self];
							var ownedSystems         = readState.archetypeToSystems[ownedArchetype];
							var previousOwnedSystems = readState.archetypeToSystems[prevArch];
							
							keptComponents.Clear();
							//Console.WriteLine("Add");
							foreach (var sys in ownedSystems)
							{
								//Console.WriteLine($"  SysId={sys}");
								if (client.Serializers.TryGetValue(sys, out var serializer)
								    && serializer.AuthorityArchetype is { } authorityArchetype)
								{
									//Console.WriteLine($"    {serializer.Identifier}");
									authorityArchetype.TryKeepAuthority(client.gameWorld.Safe(new GameEntityHandle(self)), true, keptComponents);
								}
							}

							//Console.WriteLine("Remove");
							foreach (var sys in previousOwnedSystems)
							{
								//Console.WriteLine($"  SysId={sys}");
								if (client.Serializers.TryGetValue(sys, out var serializer)
								    && serializer.AuthorityArchetype is { } authorityArchetype)
								{
									//Console.WriteLine($"    {serializer.Identifier}");
									authorityArchetype.TryKeepAuthority(client.gameWorld.Safe(new GameEntityHandle(self)), false, keptComponents);
								}
							}
						}

						client.gameWorld.UpdateOwnedComponent(new GameEntityHandle(self), new SnapshotOwnedWriteArchetype(prevCdArch));
					}
				}

				readState.FinalizeEntities();

				foreach (var (systemId, serializer) in client.Serializers)
				{
					PooledList<bool> ignoredList;
					if (!systemToEntities.TryGetValue(systemId, out var entityList))
					{
						systemToEntities[systemId] = entityList  = (new(), new());
						systemToIgnored[systemId]  = ignoredList = new PooledList<bool>();
					}
					else
					{
						ignoredList = systemToIgnored[systemId];
					}

					entityList.self.Clear();
					entityList.snapshot.Clear();
					ignoredList.Clear();
					ignoredList.AddRange(readState.dataIgnored);

					serializer.Instigator = c;

					if (serializer.SerializerArchetype is { } serializerArchetype)
					{
						if (entityUpdateList.Count > 0)
							serializerArchetype.OnDeserializerArchetypeUpdate(entityUpdateList.Span, archetypeUpdateList.Span, readState.archetypeToSystems, readState.parentOwned);
					}
				}

				foreach (var entity in client.State.GetAllEntities())
				{
					var archetypeSystems = readState.archetypeToSystems[readState.archetype[entity]];
					//Console.WriteLine($"Entity {entity} (Remote={readState.remote[entity]}) - {Thread.CurrentThread.Name}");
					//Console.WriteLine($"\tArchetype {readState.archetype[entity]}; {archetypeSystems.Length}");

					// We've already set the ignored data in top
					if (readState.dataIgnored[entity])
					{
						foreach (var sys in archetypeSystems)
						{
							if (systemToEntities.TryGetValue(sys, out var list))
							{
								// If the parent is destroyed, set the ID to 0.
								list.self.Add(new GameEntityHandle(readState.parentDestroyed[entity] ? 0 : entity));
								list.snapshot.Add(readState.snapshot[entity].Handle);
							}
						}

						continue;
					}

					foreach (var sys in archetypeSystems)
					{
						//Console.WriteLine($"\t\tsys -> {sys}");
						if (systemToEntities.TryGetValue(sys, out var list))
						{
							systemToIgnored[sys][(int) entity] = false;
							list.self.Add(new GameEntityHandle(entity));
							list.snapshot.Add(readState.snapshot[entity].Handle);
						}
					}
					
					if (readState.parentOwned[entity])
					{
						//Console.WriteLine($"\tAuthority {readState.ownedArch[entity]};");
						var ownedSystems  = parentState.GetArchetypeSystems(readState.ownedArch[entity]);
						var parentSystems = parentState.GetArchetypeSystems(parentState.GetArchetypeOfEntity(entity));
						foreach (var sys in parentSystems)
						{
							if (systemToIgnored.TryGetValue(sys, out var list))
								list[(int) entity] = true;
						}

						foreach (var sys in ownedSystems)
						{
							//Console.WriteLine($"\t\tsys -> {sys}");
							if (systemToIgnored.TryGetValue(sys, out var list))
							{
								list[(int) entity] = false;
								//Console.WriteLine($"\t\t\twill not ignore {entity}");
							}
						}
					}
					else if (readState.owned[entity])
					{
						//Console.WriteLine($"\tOwned {readState.ownedArch[entity]};");
						var ownedSystems = readState.archetypeToSystems[readState.ownedArch[entity]];
						
						foreach (var sys in ownedSystems)
						{
							//Console.WriteLine($"\t\tsys -> {sys}");
							if (systemToIgnored.TryGetValue(sys, out var list))
							{
								list[(int) entity] = true;
								//Console.WriteLine($"\t\t\twill ignore {entity}");
							}
						}
					}
				}

				var post       = new Scheduler();
				var parameters = new DeserializationParameters(readState.Tick, post);

				while (!bitBuffer.IsFinished)
				{
					var systemId = bitBuffer.ReadUIntD4();
					var length   = bitBuffer.ReadUIntD4();

					bytePool.Clear();
					var span = bytePool.AddSpan((int) length);
					bitBuffer.ReadSpan(span, (int) length);

					if (client.Serializers.TryGetValue(systemId, out var serializer))
					{
						var task = serializer.PrepareDeserializeTask(parameters, span, new ISerializer.RefData
						{
							Snapshot = systemToEntities[systemId].snapshot.Span,
							Self = systemToEntities[systemId].self.Span,
							IgnoredSet = systemToIgnored[systemId].Span
						});
						tasks.Add(task);
					}
				}

				foreach (var task in tasks)
				{
					var awaiter = task.GetAwaiter();
					while (!awaiter.IsCompleted)
					{
					}
					/*var o = task.AsTask();
					while (o == UniTaskStatus.Pending)
					{
					}*/
				}

				post.Run();

				foreach (var ent in toDestroy)
				{
					// Ignore, it seems that a new entity has been created with the same ID as the one that should have been destroyed.
					if (client.gameWorld.Safe(ent.Handle).Version != ent.Version)
						continue;

					client.gameWorld.RemoveEntity(ent.Handle);
				}
			}

			private void ReadArchetypes()
			{
				var count = bitBuffer.ReadUIntD4();

				uint prevArchId = 0;
				uint prevSysId  = 0;
				for (var i = 0; i < count; i++)
				{
					tempSystems.Clear();
					prevSysId = 0;

					var archId   = bitBuffer.ReadUIntD4Delta(prevArchId);
					var sysCount = bitBuffer.ReadUIntD4();
					for (var s = 0; s < sysCount; s++)
					{
						prevSysId = bitBuffer.ReadUIntD4Delta(prevSysId);
						tempSystems.Add(prevSysId);
					}

					prevArchId = archId;
					readState.SetArchetypeSystems(archId, tempSystems.Span);
				}
			}

			private void ReadEntityUpdateOrAdd()
			{
				// ----- DELTA VARIABLES
				// ..
				var prevLocalId       = 0u;
				var prevLocalVersion  = 0u;
				var prevRemoteId      = 0u;
				var prevRemoteVersion = 0u;
				var prevArchetype     = 0u;
				var prevInstigator    = 0;
				
				var count = bitBuffer.ReadUIntD4();
				for (var i = 0; i < count; i++)
				{
					prevLocalId       = bitBuffer.ReadUIntD4Delta(prevLocalId);
					prevLocalVersion  = bitBuffer.ReadUIntD4Delta(prevLocalVersion);
					prevRemoteId      = bitBuffer.ReadUIntD4Delta(prevRemoteId);
					prevRemoteVersion = bitBuffer.ReadUIntD4Delta(prevRemoteVersion);
					prevArchetype     = bitBuffer.ReadUIntD4Delta(prevArchetype);
					prevInstigator    = bitBuffer.ReadIntDelta(prevInstigator);

					var snapshotLocal = new GameEntity(prevLocalId, prevLocalVersion);
					var remote        = new SnapshotEntity(new GameEntity(prevRemoteId, prevRemoteVersion), prevInstigator, client.Storage);

					var self = readState.GetSelfEntity(snapshotLocal);
					if (self.Id <= 0 || snapshotLocal.Version != readState.snapshot[self.Id].Version)
					{
						if (self.Id > 0)
						{
							readState.RemoveEntity(self);
							toDestroy.Add(self);
						}

						if (prevInstigator == client.ParentInstigatorId && client.gameWorld.Exists(new GameEntity(prevRemoteId, prevRemoteVersion)))
						{
							self = new GameEntity(prevRemoteId, prevRemoteVersion);
							readState.AddEntity(self, snapshotLocal, remote, true, true, false);
						}
						else
						{
							self = client.gameWorld.Safe(client.gameWorld.CreateEntity());
							readState.AddEntity(self, snapshotLocal, remote, prevInstigator == client.ParentInstigatorId, false, prevInstigator == client.ParentInstigatorId);

							if (prevInstigator == client.ParentInstigatorId)
								readState.dataIgnored[self.Id] = true;
						}
					}

					// If the archetype changed and that it wasn't an entity created by us, add it to the update events.
					if (readState.archetype[self.Id] != prevArchetype)
					{
						readState.AssignArchetype(self.Id, prevArchetype);
						entityUpdateList.Add(self);
						archetypeUpdateList.Add(new SnapshotEntityArchetype(prevArchetype));
					}

					if (!client.gameWorld.HasComponent<SnapshotEntity>(self.Handle))
						client.gameWorld.AddComponent(self.Handle, remote);
				}
			}

			private void ReadRemovedEntity()
			{
				// ----- DELTA VARIABLES
				// ..
				var prevLocalId      = 0u;
				var prevLocalVersion = 0u;

				var count = bitBuffer.ReadUIntD4();
				for (var i = 0; i < count; i++)
				{
					prevLocalId      = bitBuffer.ReadUIntD4Delta(prevLocalId);
					prevLocalVersion = bitBuffer.ReadUIntD4Delta(prevLocalVersion);

					var self = readState.GetSelfEntity(new GameEntity(prevLocalId, prevLocalVersion));
					if (self.Id > 0)
					{
						readState.RemoveEntity(self);
						toDestroy.Add(self);
					}
				}
			}
		}
	}
}
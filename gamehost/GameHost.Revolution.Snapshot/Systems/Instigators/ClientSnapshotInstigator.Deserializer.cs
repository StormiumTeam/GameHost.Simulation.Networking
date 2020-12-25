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

			private IScheduler post = new Scheduler();

			public void Deserialize(ClientSnapshotInstigator c, Span<byte> data)
			{
				client    = c;
				readState = (ClientSnapshotState) client.State;
				
				var parentState = (BroadcastInstigator.SnapshotState) client.parent.State;

				bitBuffer.readPosition = 0;
				bitBuffer.nextPosition = 0;
				bitBuffer.AddSpan(data);

				tasks.Clear();
				toDestroy.Clear();
				entityUpdateList.Clear();
				archetypeUpdateList.Clear();
				ownedUpdateList.Clear();

				var prevTick = readState.Tick;
				readState.Tick = bitBuffer.ReadUInt();
				if (readState.Tick != prevTick + 1 && readState.Tick > 0 && prevTick > 0)
				{
					throw new InvalidOperationException($"NOT WHAT WE EXPECTED (prev={prevTick} next={readState.Tick} exp={prevTick + 1})");
					Console.WriteLine();
				}
				
				var guard = bitBuffer.ReadLong();
				if (guard != 69420)
				{
					throw new InvalidOperationException("Invalid Guard! " + guard);
				}
				
				var maxId = bitBuffer.ReadUInt();
				readState.Prepare(maxId + 1);

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

						ref var ghost = ref readState.GetRefGhost(prevLocalId);
						if (ghost.Local.Version != prevLocalVer)
							continue;

						if (ghost.ParentOwned)
						{
							throw new InvalidOperationException($"Ownership: Applying one an entity that the parent has created.\nThis is not acceptable.");
						}

						ghost.Owned     = true;
						if (ghost.OwnedArchetype != prevCdArch)
						{
							// Remove previous authorities
							var prevArch = ghost.OwnedArchetype;

							ghost.OwnedArchetype = prevCdArch;
							ownedUpdateList.Add(ghost.Self);

							var ownedSystems         = readState.archetypeToSystems[ghost.OwnedArchetype];
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
									authorityArchetype.TryKeepAuthority(ghost.Self, true, keptComponents);
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
									authorityArchetype.TryKeepAuthority(ghost.Self, false, keptComponents);
								}
							}
						}

						client.gameWorld.UpdateOwnedComponent(ghost.Self.Handle, new SnapshotOwnedWriteArchetype(prevCdArch));
					}
				}

				readState.FinalizeEntities();
				
				var msg = "";
				foreach (var ghost in readState.ghosts)
				{
					if (!ghost.IsInitialized)
						continue;
					
					msg += $"({ghost.Local}, {ghost.Self}) -> {ghost.Archetype}\n";
				}

				if (client.ParentInstigatorId != 0)
					msg = msg;
				
				// Last foreach on all entities to check whether or not it contains an entity that was destroyed in the past
				foreach (ref var ghost in readState.ghosts.AsSpan())
				{
					if (client.gameWorld.Exists(ghost.Self))
						continue;

					/*if (ghost.Self != default)
						Console.WriteLine($"ignore snapshot #{ghost.Local}");*/
					ghost.IsDataIgnored = true;
				}

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
					
					foreach (var ghost in readState.ghosts)
						ignoredList.Add(ghost.IsDataIgnored || ghost.IsInitialized == false);

					serializer.Instigator = c;

					if (serializer.SerializerArchetype is { } serializerArchetype)
					{
						for (var ent = 0; ent < entityUpdateList.Count; ent++)
						{
							var entity = entityUpdateList[ent];
							serializerArchetype.OnDeserializerArchetypeUpdate(entity, archetypeUpdateList[ent], readState.archetypeToSystems, readState.ghosts[readState.selfToSnapshot[entity].Id].ParentOwned);
						}
					}
				}

				foreach (var ghost in readState.ghosts)
				{
					if (!ghost.IsInitialized)
						continue;
					
					var archetypeSystems = readState.archetypeToSystems[ghost.Archetype];
					//Console.WriteLine($"Entity {entity} (Remote={readState.remote[entity]}) - {Thread.CurrentThread.Name}");
					//Console.WriteLine($"\tArchetype {readState.archetype[entity]}; {archetypeSystems.Length}");

					// We've already set the ignored data in top
					if (ghost.IsDataIgnored)
					{
						foreach (var sys in archetypeSystems)
						{
							if (systemToEntities.TryGetValue(sys, out var list))
							{
								// If the parent is destroyed, set the ID to 0.
								list.self.Add(new GameEntityHandle(ghost.ParentDestroyed ? 0 : ghost.Self.Id));
								list.snapshot.Add(ghost.Local.Handle);
							}
						}

						continue;
					}

					foreach (var sys in archetypeSystems)
					{
						//Console.WriteLine($"\t\tsys -> {sys}");
						if (systemToEntities.TryGetValue(sys, out var list))
						{
							systemToIgnored[sys][(int) ghost.Local.Id] = false;
							list.self.Add(ghost.Self.Handle);
							list.snapshot.Add(ghost.Local.Handle);
						}
					}
					
					if (ghost.ParentOwned)
					{
						//Console.WriteLine($"\tAuthority {readState.ownedArch[entity]};");
						var ownedSystems  = parentState.GetArchetypeSystems(ghost.OwnedArchetype);
						// snapshot to self !!!
						var parentSystems = parentState.GetArchetypeSystems(parentState.GetArchetypeOfEntity(ghost.Self.Id));
						foreach (var sys in parentSystems)
						{
							if (systemToIgnored.TryGetValue(sys, out var list))
								list[(int) ghost.Local.Id] = true;
						}

						foreach (var sys in ownedSystems)
						{
							//Console.WriteLine($"\t\tsys -> {sys}");
							if (systemToIgnored.TryGetValue(sys, out var list))
							{
								list[(int) ghost.Local.Id] = false;
								//Console.WriteLine($"\t\t\twill not ignore {entity}");
							}
						}
					}
					else if (ghost.Owned)
					{
						//Console.WriteLine($"\tOwned {readState.ownedArch[entity]};");
						var ownedSystems = readState.archetypeToSystems[ghost.OwnedArchetype];
						
						foreach (var sys in ownedSystems)
						{
							//Console.WriteLine($"\t\tsys -> {sys}");
							if (systemToIgnored.TryGetValue(sys, out var list))
							{
								list[(int) ghost.Local.Id] = true;
								//Console.WriteLine($"\t\t\twill ignore {entity}");
							}
						}
					}
				}
				
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
						serializer.Instigator = client;
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
				}

				post.Run();

				foreach (var ent in toDestroy)
				{
					// perhaps it was a past destroyed entity?
					if (!client.gameWorld.Contains(ent.Handle))
						continue;
					
					// Ignore, it seems that a new entity has been created with the same ID as the one that should have been destroyed.
					if (client.gameWorld.Safe(ent.Handle).Version != ent.Version)
						continue;

					client.gameWorld.RemoveEntity(ent.Handle);
					Console.WriteLine($"destroyed {ent}");
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

					//Console.WriteLine($"{Thread.CurrentThread.Name} - Reading Update/Add - {snapshotLocal} {remote} - arch={prevArchetype}");
					ref var ghost = ref readState.GetRefGhost(snapshotLocal.Id);
					if (!ghost.IsInitialized 
					    || !client.gameWorld.Exists(ghost.Self) 
					    || ghost.Local.Version != snapshotLocal.Version)
					{
						GameEntity self;
						bool       parentOwned;
						bool       owned;
						bool       parentDestroyed;

						var selfExist = prevInstigator == client.ParentInstigatorId && client.gameWorld.Exists(remote.Source);
						if (ghost.Local.Version != snapshotLocal.Version)
						{
							if (!selfExist && client.gameWorld.Exists(ghost.Self))
							{
								toDestroy.Add(remote.Source);
							}
							
							readState.RemoveGhost(snapshotLocal.Id);
						}

						if (selfExist)
						{
							self            = remote.Source;
							parentOwned     = owned = true;
							parentDestroyed = false;
						}
						else
						{
							self        = client.gameWorld.Safe(client.gameWorld.CreateEntity());
							parentOwned = parentDestroyed = prevInstigator == client.ParentInstigatorId;
							owned       = false;
						}

						if (client.ParentInstigatorId == 0)
							Console.WriteLine($"Add/Update Local={snapshotLocal}, Self={self}, Remote={remote}");

						readState.AddEntity(self, snapshotLocal, remote, parentOwned, owned, parentDestroyed);
						if (parentDestroyed)
						{
							ghost.IsDataIgnored = true;
						}
					}

					// If the archetype changed and that it wasn't an entity created by us, add it to the update events.
					if (ghost.Archetype != prevArchetype)
					{
						readState.AssignArchetype(snapshotLocal.Id, prevArchetype);
						entityUpdateList.Add(ghost.Self);
						archetypeUpdateList.Add(new SnapshotEntityArchetype(prevArchetype));
					}

					if (!client.gameWorld.HasComponent<SnapshotEntity>(ghost.Self.Handle))
						client.gameWorld.AddComponent(ghost.Self.Handle, remote);
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

					ref var ghost = ref readState.GetRefGhost(prevLocalId);
					if (ghost.Local.Version == prevLocalVersion)
					{
						if (ghost.Self.Id > 0)
							toDestroy.Add(ghost.Self);
						readState.RemoveGhost(ghost.Local.Id);
					}
				}
			}
		}
	}
}
using System;
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

			private readonly PooledDictionary<uint, PooledList<GameEntityHandle>> systemToEntities = new();

			private readonly PooledList<UniTask> tasks = new();

			private readonly PooledList<uint> tempSystems = new();

			private readonly PooledList<GameEntity> toDestroy = new();

			private ClientSnapshotInstigator client;
			private ClientSnapshotState      readState;

			public void Deserialize(ClientSnapshotInstigator c, Span<byte> data)
			{
				client    = c;
				readState = (ClientSnapshotState) client.State;
				readState.Prepare();

				bitBuffer.readPosition = 0;
				bitBuffer.nextPosition = 0;
				bitBuffer.AddSpan(data);

				tasks.Clear();
				toDestroy.Clear();
				entityUpdateList.Clear();
				archetypeUpdateList.Clear();
				
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
						if (readState.parentOwned[self])
						{
							throw new InvalidOperationException($"Ownership: Applying one an entity that the parent has created.\nThis is not acceptable.");
						}

						readState.owned[self] = true;
						client.gameWorld.UpdateOwnedComponent(new GameEntityHandle(self), new SnapshotOwnedWriteArchetype(prevCdArch));
					}
				}

				readState.FinalizeEntities();

				foreach (var (systemId, serializer) in client.Serializers)
				{
					if (!systemToEntities.TryGetValue(systemId, out var entityList))
						systemToEntities[systemId] = entityList = new PooledList<GameEntityHandle>();

					entityList.Clear();

					serializer.Instigator = c;
					
					if (entityUpdateList.Count > 0
					    && serializer.SerializerArchetype is { } serializerArchetype)
					{
						serializerArchetype.OnDeserializerArchetypeUpdate(entityUpdateList.Span, archetypeUpdateList.Span, readState.archetypeToSystems);
					}
				}

				foreach (var entity in client.State.GetAllEntities())
				{
					var archetype = client.gameWorld.HasComponent<SnapshotOwnedWriteArchetype>(new GameEntityHandle(entity))
						? client.gameWorld.GetComponentData<SnapshotOwnedWriteArchetype>(new GameEntityHandle(entity)).OwnedArchetype
						: readState.archetype[entity];
					
					var archetypeSystems = readState.archetypeToSystems[archetype];
					foreach (var sys in archetypeSystems)
					{
						if (systemToEntities.TryGetValue(sys, out var list))
							list.Add(new GameEntityHandle(entity));
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

					if (client.Serializers.TryGetValue(systemId, out var serializer)) tasks.Add(serializer.PrepareDeserializeTask(parameters, span, systemToEntities[systemId].Span));
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
					while (sysCount-- > 0)
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
					var remote        = new SnapshotEntity(new GameEntity(prevRemoteId, prevRemoteVersion), prevInstigator);
					
					var self = readState.GetSelfEntity(snapshotLocal);
					if (self.Id <= 0 || snapshotLocal.Version != readState.snapshot[self.Id].Version)
					{
						if (self.Id > 0)
						{
							readState.RemoveEntity(self);
							toDestroy.Add(self);
						}

						if (prevInstigator == client.ParentInstigatorId
						    && client.gameWorld.Contains(new GameEntityHandle(prevRemoteId)))
						{
							self = client.gameWorld.Safe(new GameEntityHandle(prevRemoteId));
							readState.AddEntity(self, snapshotLocal, remote, true, true);
						}
						else
						{
							self = client.gameWorld.Safe(client.gameWorld.CreateEntity());
							readState.AddEntity(self, snapshotLocal, remote, false, false);
						}
					}

					// If the archetype changed and that it wasn't an entity created by us, add it to the update events.
					// TODO: Check for permissions instead of always forbidding archetype update on previously created entities.
					if (readState.archetype[self.Id] != prevArchetype && !readState.parentOwned[self.Id])
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
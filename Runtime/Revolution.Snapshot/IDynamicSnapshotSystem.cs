using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace Revolution
{
	/// <summary>
	/// Represent a system that can be executed dynamically with chunks and entities
	/// </summary>
	public interface IDynamicSnapshotSystem
	{
		ref SharedSystemChunk GetSharedChunk();
		ref SharedSystemGhost GetSharedGhost();
		
		bool                  IsChunkValid(ArchetypeChunk                       chunk);
		void                  OnDeserializerArchetypeUpdate(NativeArray<Entity> entities, NativeArray<uint> archetypes, Dictionary<uint, NativeArray<uint>> archetypeToSystems);
	}
}
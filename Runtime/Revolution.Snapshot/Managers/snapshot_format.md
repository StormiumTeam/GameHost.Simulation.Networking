## Snapshot Format
````csharp
uint entity_update_count; // How many entities had their archetype changed?

packed_uint("tick");
// -- 'entity_count' is set to 0 if no ghosts were added or removed
packed_uint("entity_count");
if (entity_count > 0) {
	write_missing_archetype();
	// -- temporaly used until unity fix the bug after a writer.flush()
	byte("seperator"); //< 42
	
	uint previousGhostId;
	uint previousArchetypeId;
	
	// The client will know if a ghost was added or removed.
	foreach (ghost in ghostArray) {
		packed_uint_delta("ghost_id", previousGhostId);
		// -- for now, we write the archetype of all ghosts
		// -- in future it will be optimized to only write the changed ghosts.
		packed_uint_delta("ghost_arch", previousArchetypeId);
		
		previousGhostIndex = ghost.id;
		previousArchetypeId = ghost.arch;
	}
} else if (entity_update_count > 0) {
	packed_uint("entity_update_count");
	// -- be sure to only read incoming archetypes if 'entity_update_count' is superior than 0!
	write_missing_archetype();
	// -- temporaly used until unity fix the bug after a writer.flush()
	byte("seperator"); //< 42
	
	uint previousGhostIndex;
	uint previousArchetypeId;
	
	// We use the index instead of the id, so delta compression will do a better job here.
	foreach (change in entity_update) {
		packed_uint_delta("ghost_index", previousGhostIndex);
		packed_uint_delta("ghost_arch", previousArchetypeId);
		
		previousGhostIndex = change.ghostIndex;
		previousArchetypeId = change.arch;
	}
}

// -- Write the rest of the data from systems
system_snapshot_data();

write_missing_archetype() {
	packed_uint("new_archetype_count");
	uint previousArchetypeId;
	foreach (arch in new_archetypes)
	{
		packed_uint_delta("arch_id", previousArchetypeId);
		packed_uint("arch_system_count");
		foreach (system in arch.systems)
		{
			packed_uint("system_id");
		}
		
		previousArchetypeId = arch.id;
	}
}
````
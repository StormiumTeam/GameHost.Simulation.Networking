## Snapshot Format
### Notes
- Those use compressed data (bool is transformed into one bit, zig-zag encoding, delta encoding, ...)
- Delta encoding is done in this way (Writing: `Write(Value - Baseline); Baseline = Value;`; Reading: `Value += Read(Baseline); Baseline = Value;`)

````csharp
var tick = uint();
var isRemake = bool();
if (isRemake) {
	archetypes_data();
	entities();

	// -- Removed entities
	uint prevLocalId;
	uint prevLocalVersion;

	var removedCount = uintD4();
	while (removedCount-->0) {
		var localId      = uintD4Delta(prevLocalId);
		var localVersion = uintD4Delta(prevLocalVersion);

		prevLocalId      = localId;
		prevLocalVersion = localVersion;
	} 
} else {
	archetypes_data();
	entities();
}

// -- Write the rest of the data from systems
while (!isFinishedReading) {
	var systemId = uintD4();
	var length = uintD4();

	// ...
	var systemData = new byte[length];
	readArray(systemData, length); 
}

archetypes_data() {
	var newArchetypeCount = uintD4();
	uint previousArchetypeId;
	foreach (arch in new_archetypes)
	{
		uintD4Delta(arch.Id, previousArchetypeId);
		uintD4(arch.systems.count);

		uint previousSystemId;
		foreach (system in arch.systems)
		{
			uintD4Delta(system.Id, previousSystemId);
			previousSystemId = system.Id;
		}
		
		previousArchetypeId = arch.id;
	}
}

entities() {
	// Delta variables
	uint prevLocalId;
	uint prevLocalVersion;
	uint prevRemoteId;
	uint prevRemoteVersion;
	uint prevArchetype;
	int  prevInstigator;

	var updateCount = uintD4();
	while (updateCount-->0) {
		var localId       = uintD4Delta(prevLocalId);
		var localVersion  = uintD4Delta(prevLocalVersion);
		var remoteId      = uintD4Delta(prevRemoteId);
		var remoteVersion = uintD4Delta(prevRemoteVersion);
		var archetype     = uintD4Delta(prevArchetype);
		var instigator    = uintD4Delta(prevInstigator);

		prevLocalId      = localId;
		prevLocalVersion = localVersion;
		// ^ do the same for prevRemoteId, prevRemoteVersion... prevInstigator.
	}
}
````
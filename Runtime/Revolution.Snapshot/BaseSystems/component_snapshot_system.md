## Basic
This is the most basic way to transfer snapshots between clients.

**Pro:**
1. The basic snapshot system is the fastest way to transfer snapshots.
2. It's the best way to transfer snapshots that change every frame.

**Cons:**
1. A basic snapshot system can't use prediction 
2. A basic snapshot system can't block serialization of non-changed components (use a delta system instead)
3. If the snapshot don't change a lot and is big, use a delta system instead.

**Usage:**
```csharp
struct Health : IComponentData
{
	public int Value, Max;
}

struct HealthSnapshot : IReadWriteSnapshot<HealthSnapshot>, ISynchronizeImpl<Health>
{
	public int Tick { get; set; }
	public int Value, Max;
	
	public void WriteTo(DataStreamWriter writer, ref HealthSnapshot baseline, NetworkCompressionModel compressionModel)
	{
		writer.WritePackedIntDelta(Value, baseline.Value, compressionModel);
		writer.WritePackedIntDelta(Max, baseline.Max, compressionModel);
	}

	public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref HealthSnapshot baseline, NetworkCompressionModel compressionModel)
	{
		Value = reader.ReadPackedIntDelta(ref ctx, baseline.Value, compressionModel);
		Max   = reader.ReadPackedIntDelta(ref ctx, baseline.Max, compressionModel);
	}

	public void SynchronizeFrom(in Health component)
	{
		Value = component.Value;
		Max   = component.Value;
	}

	public void SynchronizeTo(ref Health component)
	{
		component.Value = Value;
		component.Max   = Max;
	}
}

public class HealthSnapshotSystem : ComponentSnapshotSystem_Basic<Health, HealthSnapshot>
{
	public struct Exclude : IComponentData
	{
	}

	public override ComponentType ExcludeComponent => typeof(Exclude);
}
```

## Delta
A snapshot delta system will offer the possibility to block serialization on non-changed components.

**Settings:**  
- `DeltaType`: Choice between blocking chunk serialization or component serialization.  
	1. `DeltaChange.Chunk`: Block serialization if no chunk were changed
	2. `DeltaChange.Component`: Block serialization if the component was not changed.
	3. `DeltaChange.Both`: Block both

**Pro:**
1. If your components don't change a lot (eg: settings, static positions), this system is the best choice.
2. If 'Component' is used for delta verification, you need to manually check if the snapshot is equal or not to the baseline.

**Cons:**
1. Can't use prediction
2. Enabling both 'Chunk' and 'Component' delta verification can leave waste, use it wisely.

**Tips:**
1. It's preferable to enable 'Component' delta verification, but if your chunk don't change, there is no need to activate this option.
2. Only enable 'Chunk' delta verification if you are sure your chunk components don't change.
3. If 'Component' delta verification is enabled and you check for positions values, it's better to check for a gap between the current snapshot and the baseline instead of checking if the values are the same.

**Usage:**
```csharp
public struct TranslationSnapshot : IReadWriteSnapshot<TranslationSnapshot>, ISynchronizeImpl<Translation>, ISnapshotDelta<TranslationSnapshot>
{
	public uint Tick { get; set; }

	public int3 Vector;

	public void WriteTo(DataStreamWriter writer, ref TranslationSnapshot baseline, NetworkCompressionModel compressionModel)
	{
		for (var i = 0; i != 3; i++)
			writer.WritePackedIntDelta(Vector[i], baseline.Vector[i], compressionModel);
	}

	public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref TranslationSnapshot baseline, NetworkCompressionModel compressionModel)
	{
		for (var i = 0; i != 3; i++)
			Vector[i] = reader.ReadPackedIntDelta(ref ctx, baseline.Vector[i], compressionModel);
	}

	public void SynchronizeFrom(in Translation component)
	{
		Vector = (int3) (component.Value * 1000);
	}

	public void SynchronizeTo(ref Translation component)
	{
		component.Value = (float3) Vector * 0.001f;
	}

	public bool DidChange(TranslationSnapshot baseline)
	{
		return math.distance(Vector, baseline.Vector) > 10;
	}
}

public class TranslationSnapshotSystem : ComponentSnapshotSystem_Delta<Translation, TranslationSnapshot>
{
	public struct Exclude : IComponentData
	{
	}

	public override ComponentType ExcludeComponent => typeof(Exclude);
}

```
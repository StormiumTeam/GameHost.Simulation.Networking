# Snapshot
- [What is a snapshot?](#What)   
- [How is this done for this library?](#How)   
- [Creation of a simple snapshot system](#Creation)   

## What
---
todo ...

## How
---
todo ...

## Creation
---

You'll be mostly interested at sending/receiving entities data.
Lucky for you, this library provide you two class to get started:
- `SnapshotEntityDataAutomaticStreamer` (automatically burstable)
- `SnapshotEntityDataManualStreamer` (burstable in some condition)    

### Usage   
- For the simplest way (eg: only sending data with no information on other entities), create a new class based on `SnapshotEntityDataAutomaticStreamer`:
```c#
struct MyDataState : IComponentData
{
    public int Value;
}

public class MyEntityStreamer : SnapshotEntityDataAutomaticStreamer<MyDataState>
{
}
```
This will automatically add the system into the snapshot manager.

- For a more manual way, in case your data has a reference to another entity (or if you need to strip some data, send managed data), create a new class based on `SnapshotEntityDataManualStreamer`
```c#
struct MyComplexData : IComponentData
{
    public float3 CompressThis;
    public Entity EntityReference;
}

// You also have the possibility to seperate this into two payload (write and read) (eg: if you have non burstable function in the Read function).
public struct Payload : IMultiEntityDataPayload
{
    private ComponentDataFromEntity<MyComplexData> DataFromEntity;

    void Write(int index, Entity entity, DataBufferWriter data, SnapshotReceiver receiver, StSnapshotRuntime runtime);
    {
        var complexData = DataFromEntity[entity];

        data.WriteValue((half3) complexData.CompressThis);
        data.WriteValue(complexData.EntityReference);
    }

    void Read(int index, Entity entity, ref DataBufferReader data, SnapshotSender sender, StSnapshotRuntime runtime)
    {
        var compressedFloat3 = data.ReadValue<half3>();
        var referencedEntity = data.ReadValue<Entity>();

        DataFromEntity[entity] = new MyComplexData
        {
            CompressThis = compressedFloat3,
            EntityReference = runtime.EntityToWorld(referencedEntity);
        };
    }
}

public class MyComplexEntityStreamer : SnapshotEntityDataAutomaticStreamer<MyComplexData>
{
    protected override void UpdatePayload(ref TMultiEntityPayload current)
    {
        current.DataFromEntity = GetComponentDataFromEntity<MyComplexData>();
    }
}
```

todo (advanced usage (eg: not only about entities))...
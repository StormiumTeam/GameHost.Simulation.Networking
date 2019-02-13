# Data Buffers
This package offer two structures for managing your data:
- [Markers](#DataBufferMarker)
- [For writing: DataBufferWriter](#DataBufferWriter)
- [For reading: DataBufferReader](#DataBufferReader)
- [Dynamic Integers](#DynamicIntegers)
- [Extensions](#Extensions)

## DataBufferMarker
---
Markers are used to write or read at a specific location in the buffer.

# Marker creations example
```c#
var marker = default(DataBufferMarker);
if (!marker.Valid)
    Debug.Log("Marker is not valid, as expected.")

marker = new DataBufferMarker(index: 4);
marker = dataBuffer.CreateMarker(index: 4);

// Get a new marker from an already existing one (with an offset).
marker = marker.GetOffset(2); // index = 4 + 2 = 6
```

# Marker operations example
```c#
// Allocate a new buffer with 0 capacity and Temp allocation.
using (var buffer = new DataBufferWriter(0, Allocator.Temp))
{
    buffer.WriteByte(7);
    // We get a marker so we can overwrite a data at this location later
    var marker = buffer.WriteByte(8);
    buffer.WriteByte(9);

    // Our current buffer data:
    // [0] = 7
    // [1] = 8
    // [2] = 9
    
    // Now let's overwrite some data...
    buffer.WriteByte(42);

    // The new current buffer data:
    // [0] = 7
    // [1] = 42
    // [2] = 9    
}
```

## DataBufferWriter
---
The struct DataBufferWriter give you the possibility to write (almost) unlimited data to an automatic growing list.

For normal usage, you can write to the buffer with these methods:  
- Reference Based Writing (for micro-optimization):
```c#
DataBufferMarker WriteRef<T>(ref T value, DataBufferMarker marker = default) where T : struct;
```
- Copy Based Writing (if you can't reference a value):
```c#
DataBufferMarker WriteValue<T>(T value, DataBufferMarker marker = default) where T : struct;
DataBufferMarker WriteByte(byte value, DataBufferMarker marker = default);
DataBufferMarker WriteShort(short value, DataBufferMarker marker = default);
DataBufferMarker WriteInt(int value, DataBufferMarker marker = default);
DataBufferMarker WriteLong(long value, DataBufferMarker marker = default);
```
- Static Based Writing Markers (you can't use markers at all for static operation)
```c#
void WriteStatic(string str);
void writeBuffer(DataBufferWriter writer);
```

## DataBufferReader
---
The struct DataBufferReader give you the possibility to read data (from pointer, buffer, array...).

For normal usage, you can read from the buffer with these methods:
- Reading blittable structs:
```c#
T ReadValue<T>(DataBufferMarker marker = default) where T : struct;
```
- Reading a string:
```c#
string ReadString(DataBufferMarker marker = default);
```

## DynamicIntegers
---
The librairy give you the possibility to have dynamic integer that can occupy less place in the buffer.

- WriteDynamicInt/ReadDynamicInt (small size for big integer type, big size for small integer type);
```c#
int myInt = 128; // size = 4
long myLong = 96; // size = 8
int myBigInt = short.MaxValue * 2; // size = 4
int myBigLong = short.MaxValue * 2; // size = 8

// This function will allocate a byte and a new dynamically sized integer.
// This is the more costy operation as you'll write one bit for each integer.
dataBuffer.WriteDynamicInt(myInt); // size = byte + byte = 2
dataBuffer.WriteDynamicInt(myLong); // size = byte + byte = 2
dataBuffer.WriteDynamicInt(myBigInt); // size = byte + int = 5
dataBuffer.WriteDynamicInt(myBigLong); // size = byte + int = 5

myInt = reader.ReadDynamicInt();
myLong = reader.ReadDynamicInt();
myBigInt = reader.ReadDynamicInt();
myBigLong = reader.ReadDynamicInt();
```

- WriteDynamicIntWithMask/ReadDynamicIntFromMask
```c#
byte myInt1 = 100; // size = 1
short myInt2 = 1000; // size = 2
int myInt3 = 10000; // size = 4
long myInt4 = 100000: // size = 8
// total = 15

// This function will allocate a byte mask and 4 dynamically sized integer.
// This is the less costy operation in term of data size as there will be only
// one byte for managing MULTIPLE int size.
dataBuffer.WriteDynamicIntWithMask(myInt1, myInt2, myInt3, myInt4); // size = byte + byte + short + short + int = 10
reader.ReadDynamicIntFromMask(out myInt1, out myInt2, out myInt3, out myInt4);
```
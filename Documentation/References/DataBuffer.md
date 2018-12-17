# Data Buffers
This package offer two structures for managing your data:
- [Markers](#DataBufferMarker)
- [For writing: DataBufferWriter](#DataBufferWriter)
- [For reading: DataBufferReader](#DataBufferReader)

## DataBufferMarker
---
Markers are used to write or read at a specific location in the buffer.

## DataBufferWriter
---
The struct DataBufferWriter give you the possibility to write into a native list.

For normal usage, you can write to the buffer with these methods:  
- Reference Based Writing (for performance):
```c#
DataBufferMarker Write<T>(ref T value, DataBufferMarker marker = default);
// If you overwrite an existing string, you will not be able to have a bigger string size than previous one.
DataBufferMarker Write(string str, DataBufferMarker marker = default);
```
- Copy Based Writing (if you can't reference a value):
```c#
DataBufferMarker CpyWrite<T>(T value, DataBufferMarker marker = default);
```
- Static Writing Markers (only string)
```c#
void WriteStatic(string str);
```

## DataBufferReader
---
The struct DataBufferReader give you the possibility to read data (from pointer, buffer, array...).

For normal usage, you can read from the buffer with these methods:
- Reading blittable structs:
```c#
T ReadValue<T>(DataBufferMarker marker = default);
```
- Reading a string:
```c#
T ReadString(DataBufferMarker marker = default);
```
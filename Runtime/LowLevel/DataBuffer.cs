using System;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using StormiumShared.Core.Networking;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public unsafe struct DataBufferMarker
    {
        public bool Valid;
        public int  Index;

        public DataBufferMarker(int index)
        {
            Index = index;
            Valid = true;
        }

        public DataBufferMarker GetOffset(int offset)
        {
            return new DataBufferMarker(Index + offset);
        }
    }

    public unsafe partial struct DataBufferWriter : IDisposable
    {
        internal struct DataBuffer
        {
            public byte* buffer;
            public int   length;
            public int   capacity;
        }

        private Allocator   m_Allocator;
        
        [NativeDisableUnsafePtrRestriction]
        private DataBuffer* m_Data;

        public int Length
        {
            get => m_Data->length;
            set => m_Data->length = value;
        }

        public int Capacity
        {
            get => m_Data->capacity;
            set
            {
                var dataCapacity = m_Data->capacity;
                if (dataCapacity == value)
                    return;

                if (dataCapacity > value)
                    throw new InvalidOperationException("New capacity is shorter than current one");

                var newBuffer = (byte*) UnsafeUtility.Malloc(value, UnsafeUtility.AlignOf<byte>(), m_Allocator);

                UnsafeUtility.MemCpy(newBuffer, m_Data->buffer, m_Data->length);
                UnsafeUtility.Free(m_Data->buffer, m_Allocator);

                m_Data->buffer   = newBuffer;
                m_Data->capacity = value;
            }
        }

        public IntPtr GetSafePtr() => (IntPtr) m_Data->buffer;


        public DataBufferWriter(int capacity, Allocator allocator)
        {
            m_Allocator = allocator;

            m_Data           = (DataBuffer*) UnsafeUtility.Malloc(sizeof(DataBuffer), UnsafeUtility.AlignOf<DataBuffer>(), allocator);
            m_Data->buffer   = (byte*) UnsafeUtility.Malloc(capacity, UnsafeUtility.AlignOf<byte>(), allocator);
            m_Data->length   = 0;
            m_Data->capacity = capacity;
        }

        public void UpdateReference()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetWriteInfo(int size, DataBufferMarker marker)
        {
            var writeIndex = marker.Valid ? marker.Index : m_Data->length;

            TryResize(writeIndex + size);

            return writeIndex;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TryResize(int newCapacity)
        {
            if (m_Data->capacity >= newCapacity) return;

            Capacity = newCapacity * 2;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteData(byte* data, int index, int length)
        {
            UnsafeUtility.MemCpy(m_Data->buffer + index, data, (uint) length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DataBufferMarker WriteDataSafe(byte* data, int writeSize, DataBufferMarker marker)
        {
            var depointed = *m_Data;
            
            int dataLength = depointed.length, 
                writeIndex = math.select(dataLength, marker.Index, marker.Valid);
            
            // Copy from GetWriteInfo()

            var predictedLength = writeIndex + writeSize;
            
            // Copy from TryResize()
            if (depointed.capacity < predictedLength)
            {
                Capacity = math.mul(predictedLength, 2);
                depointed = *m_Data; // need to update our depointed data as we modified the capacity
            }
            
            // Copy from WriteData()
            UnsafeUtility.MemCpy(depointed.buffer + writeIndex, data, (uint) writeSize);

            m_Data->length = math.max(predictedLength, dataLength);

            var rm = default(DataBufferMarker);
            rm.Valid = true;
            rm.Index = writeIndex;
            
            return rm;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DataBufferMarker WriteRef<T>(ref T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            return WriteDataSafe((byte*) UnsafeUtility.AddressOf(ref val), UnsafeUtility.SizeOf<T>(), marker);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DataBufferMarker WriteUnmanaged<T>(T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : unmanaged
        {
            return WriteDataSafe((byte*) &val, sizeof(T), marker);
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DataBufferMarker WriteValue<T>(T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            return WriteDataSafe((byte*) UnsafeUtility.AddressOf(ref val), UnsafeUtility.SizeOf<T>(), marker);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DataBufferMarker CreateMarker(int index)
        {            
            DataBufferMarker marker = default;
            marker.Valid = true;
            marker.Index = index;
            return marker;
        }

        public void Dispose()
        {
            UnsafeUtility.Free(m_Data->buffer, m_Allocator);
            UnsafeUtility.Free(m_Data, m_Allocator);

            m_Data = null;
        }
    }

    public unsafe partial struct DataBufferWriter
    {
        public DataBufferMarker WriteByte(byte val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(byte), marker);
        }

        public DataBufferMarker WriteShort(short val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(short), marker);
        }

        public DataBufferMarker WriteInt(int val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(int), marker);
        }

        public DataBufferMarker WriteLong(long val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(long), marker);
        }

        public DataBufferMarker WriteString(string val, Encoding encoding = null, DataBufferMarker marker = default(DataBufferMarker))
        {
            fixed (char* strPtr = val)
            {
                return WriteString(strPtr, val.Length, encoding, marker);
            }
        }

        public DataBufferMarker WriteString(char* val, int strLength, Encoding encoding = null, DataBufferMarker marker = default(DataBufferMarker))
        {
            // If we have a null encoding, let's get the default one (UTF8)
            encoding = encoding ?? Encoding.UTF8;

            var   returnMarker = default(DataBufferMarker);
            void* tempCpyPtr   = null;

            // ------------------------------------------ //
            // Variables if we are writing to a marker
            // ------------------------------------------ //
            // Get the previous text size from the marker...
            var oldCpyLength = -1;
            // Difference between text size and buffer size
            var sizeDiff = 0;
            // The previous end index before re-writing the data
            var endIndex     = -1;
            var oldStrLength = -1;
            if (marker.Valid)
            {
                // Read the data from this buffer
                var reader = new DataBufferReader(GetSafePtr(), Length);
                // Start reading from the current marker index.
                var readerMarker = reader.CreateMarker(marker.Index);
                // Get the previous text size from the marker...
                oldCpyLength = reader.ReadValue<int>(readerMarker);
                // Get the difference.
                sizeDiff = math.abs(Length - oldCpyLength);
                // Get the previous end index (we add an offset to the marker)
                endIndex     = reader.ReadValue<int>(readerMarker.GetOffset(sizeof(int)));
                oldStrLength = reader.ReadValue<int>(readerMarker.GetOffset(sizeof(int) * 2));
            }

            try
            {
                // Get the length of a 'UTF8 char' * 'string length';
                var cpyLength = encoding.GetMaxByteCount(strLength);
                if (cpyLength > oldCpyLength && oldCpyLength >= 0)
                {
                    Debug.LogWarning($"Rewritten string is longer (cpymem: {cpyLength} > {oldCpyLength}, str: {strLength} > {oldStrLength})");
                    cpyLength = oldCpyLength;
                    strLength = oldStrLength;
                }

                // Allocate a temp memory region, and then...
                tempCpyPtr = UnsafeUtility.Malloc(cpyLength, 4, Allocator.Temp);
                // ... Get the bytes from the char array
                encoding.GetBytes(val, strLength, (byte*) tempCpyPtr, cpyLength);

                // Write the length of the string to the current index from the marker (or buffer if default)
                returnMarker = WriteInt(cpyLength, marker);
                // This integer give us the possilibity to know where will be our next values
                // If we update the string with a smaller length, we need to know where our next values are.
                var endMarker = WriteInt(0, returnMarker.GetOffset(sizeof(int)));
                // Write the string buffer data
                WriteInt(strLength, returnMarker.GetOffset(sizeof(int) * 2)); // In future, we should get a better way to define that
                WriteDataSafe((byte*) tempCpyPtr, cpyLength - sizeDiff, returnMarker.GetOffset(sizeof(int) * 3));
                // Re-write the end integer from end marker
                WriteInt(endIndex < 0 ? Length : endIndex, endMarker);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                // If we had no problem with our temporary allocation, free it.
                if (tempCpyPtr != null)
                    UnsafeUtility.Free(tempCpyPtr, Allocator.Temp);
            }

            return returnMarker;
        }

        public void WriteDynamicInt(ulong integer)
        {
            if (integer == 0)
            {
                WriteUnmanaged<byte>((byte) 0);
            }
            else if (integer <= byte.MaxValue)
            {
                WriteByte((byte) sizeof(byte));
                WriteUnmanaged<byte>((byte) integer);
            }
            else if (integer <= ushort.MaxValue)
            {
                WriteByte((byte) sizeof(ushort));
                WriteUnmanaged((ushort) integer);
            }
            else if (integer <= uint.MaxValue)
            {
                WriteByte((byte) sizeof(uint));
                WriteUnmanaged((uint) integer);
            }
            else
            {
                WriteByte((byte) sizeof(ulong));
                WriteUnmanaged(integer);
            }
        }
        
        public void WriteDynamicIntWithMask(in ulong r1, in ulong r2)
        {
            byte setval(ref DataBufferWriter data, in ulong i)
            {
                if (i <= byte.MaxValue)
                {
                    data.WriteUnmanaged((byte) i);
                    return 0;
                }

                if (i <= ushort.MaxValue)
                {
                    data.WriteUnmanaged((ushort) i);
                    return 1;
                }

                if (i <= uint.MaxValue)
                {
                    data.WriteUnmanaged((uint) i);
                    return 2;
                }

                data.WriteUnmanaged(i);
                return 3;
            }

            var maskMarker = WriteByte(0);
            var m1         = setval(ref this, r1);
            var m2         = setval(ref this, r2);

            WriteByte((byte) (m1 | (m2 << 2)), maskMarker);
        }
        
        public void WriteDynamicIntWithMask(in ulong r1, in ulong r2, in ulong r3)
        {
            byte setval(ref DataBufferWriter data, in ulong i)
            {
                if (i <= byte.MaxValue)
                {
                    data.WriteUnmanaged((byte) i);
                    return 0;
                }

                if (i <= ushort.MaxValue)
                {
                    data.WriteUnmanaged((ushort) i);
                    return 1;
                }

                if (i <= uint.MaxValue)
                {
                    data.WriteUnmanaged((uint) i);
                    return 2;
                }

                data.WriteUnmanaged(i);
                return 3;
            }

            var maskMarker = WriteByte(0);
            var m1 = setval(ref this, r1);
            var m2 = setval(ref this, r2);
            var m3 = setval(ref this, r3);

            WriteByte((byte) (m1 | (m2 << 2) | (m3 << 4)), maskMarker);
        }

        public void WriteDynamicIntWithMask(in ulong r1, in ulong r2, in ulong r3, in ulong r4)
        {
            byte setval(ref DataBufferWriter data, in ulong i)
            {
                if (i <= byte.MaxValue)
                {
                    data.WriteUnmanaged((byte) i);
                    return 0;
                }

                if (i <= ushort.MaxValue)
                {
                    data.WriteUnmanaged((ushort) i);
                    return 1;
                }

                if (i <= uint.MaxValue)
                {
                    data.WriteUnmanaged((uint) i);
                    return 2;
                }

                data.WriteUnmanaged(i);
                return 3;
            }

            var maskMarker = WriteByte(0);
            var m1         = setval(ref this, r1);
            var m2         = setval(ref this, r2);
            var m3         = setval(ref this, r3);
            var m4         = setval(ref this, r4);

            WriteByte((byte) (m1 | (m2 << 2) | (m3 << 4) | (m4 << 6)), maskMarker);
        }

        public void WriteBuffer(DataBufferWriter dataBuffer)
        {
            WriteDataSafe((byte*) dataBuffer.GetSafePtr(), dataBuffer.Length, default(DataBufferMarker));
        }
        
        public void WriteBuffer(DataBufferWriterFixed dataBufferFixed)
        {
            WriteDataSafe((byte*) dataBufferFixed.GetSafePtr(), dataBufferFixed.Cursor, default(DataBufferMarker));
        }

        public void WriteStaticString(string val, Encoding encoding = null)
        {
            fixed (char* strPtr = val)
            {
                WriteStaticString(strPtr, val.Length, encoding);
            }
        }

        public void WriteStaticString(char* val, int strLength, Encoding encoding = null)
        {
            // If we have a null encoding, let's get the most used one (UTF8)
            encoding = encoding ?? Encoding.UTF8;

            void* tempCpyPtr = null;

            try
            {
                // Get the length of a 'UTF8 char' * 'string length';
                var cpyLength = encoding.GetMaxByteCount(strLength);
                // Allocate a temp memory region, and then...
                tempCpyPtr = UnsafeUtility.Malloc(cpyLength, 4, Allocator.Temp);
                // ... Get the bytes from the char array
                encoding.GetBytes(val, strLength, (byte*) tempCpyPtr, cpyLength);

                // Write the length of the string to the current index of the buffer
                WriteInt(cpyLength);
                var endMarker = WriteInt(0);
                // Write the string buffer data
                WriteInt(strLength); // In future, we should get a better way to define that
                WriteDataSafe((byte*) tempCpyPtr, cpyLength, default(DataBufferMarker));
                // Re-write the end integer from end marker
                Debug.Log("Length(0)=" + Length);
                var l = Length;
                WriteInt(Length, endMarker);
                Debug.Log("Length(1)=" + Length);
                
                Debug.Log($"0= " + cpyLength);
                Debug.Log($"1= " + l);
                Debug.Log($"2= " + strLength);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                // If we had no problem with our temporary allocation, free it.
                if (tempCpyPtr != null)
                    UnsafeUtility.Free(tempCpyPtr, Allocator.Temp);
            }
        }
    }

    public unsafe struct DataBufferReader
    {
        [NativeDisableUnsafePtrRestriction]
        public byte* DataPtr;

        public int CurrReadIndex;
        public int Length;

        public DataBufferReader(IntPtr dataPtr, int length) : this((byte*) dataPtr, length)
        {
        }

        public DataBufferReader(byte* dataPtr, int length)
        {
            if (dataPtr == null)
                throw new InvalidOperationException("dataPtr is null");

            DataPtr       = dataPtr;
            CurrReadIndex = 0;
            Length        = length;
        }

        public DataBufferReader(DataBufferReader reader, int start, int end)
        {
            DataPtr       = (byte*) ((IntPtr) reader.DataPtr + start);
            CurrReadIndex = 0;
            Length        = end - start;
        }

        public DataBufferReader(DataBufferWriter writer)
        {
            DataPtr       = (byte*) writer.GetSafePtr();
            CurrReadIndex = 0;
            Length        = writer.Length;
        }

        public DataBufferReader(NativeArray<byte> data)
        {
            if (!data.IsCreated)
                throw new InvalidOperationException("data is not created");

            DataPtr       = (byte*) NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(data);
            CurrReadIndex = 0;
            Length        = data.Length;
        }

        public int GetReadIndex(DataBufferMarker marker)
        {
            var readIndex = !marker.Valid ? CurrReadIndex : marker.Index;
            if (readIndex >= Length)
            {
                throw new IndexOutOfRangeException("p1");
            }

            return readIndex;
        }

        public int GetReadIndexAndSetNew(DataBufferMarker marker, int size)
        {
            var readIndex = !marker.Valid ? CurrReadIndex : marker.Index;
            if (readIndex >= Length)
            {
                throw new IndexOutOfRangeException($"p1 r={readIndex} >= l={Length}");
            }

            CurrReadIndex = readIndex + size;
            if (CurrReadIndex > Length)
            {
                throw new IndexOutOfRangeException("p2");
            }

            return readIndex;
        }

        public void ReadUnsafe(byte* data, int index, int size)
        {
            Unsafe.CopyBlock(data, (void*) IntPtr.Add((IntPtr)DataPtr, index), (uint) size);
        }

        public T ReadValue<T>(DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            var val       = default(T);
            var size      = Unsafe.SizeOf<T>();
            var readIndex = GetReadIndexAndSetNew(marker, size);

            // Set it for later usage
            CurrReadIndex = readIndex + size;
            // Read the value
            ReadUnsafe((byte*) Unsafe.AsPointer(ref val), readIndex, size);

            return val;
        }

        public DataBufferMarker CreateMarker(int index)
        {
            return new DataBufferMarker(index);
        }

        public ulong ReadDynInteger(DataBufferMarker marker = default(DataBufferMarker))
        {
            var byteCount = ReadValue<byte>();

            if (byteCount == 0) return 0;
            if (byteCount == sizeof(byte)) return ReadValue<byte>();
            if (byteCount == sizeof(ushort)) return ReadValue<ushort>();
            if (byteCount == sizeof(uint)) return ReadValue<uint>();
            if (byteCount == sizeof(ulong)) return ReadValue<ulong>();

            throw new InvalidOperationException($"Expected byte count range: [{sizeof(byte)}..{sizeof(ulong)}], received: {byteCount}");
        }
        
        public void ReadDynIntegerFromMask(out ulong r1, out ulong r2)
        {
            void getval(ref DataBufferReader data, int mr, ref ulong i)
            {
                if (mr == 0) i = data.ReadValue<byte>();
                if (mr == 1) i = data.ReadValue<ushort>();
                if (mr == 2) i = data.ReadValue<uint>();
                if (mr == 3) i = data.ReadValue<ulong>();
            }

            var mask = ReadValue<byte>();
            var val1 = (mask & 3);
            var val2 = (mask & 12) >> 2;

            r1 = default;
            r2 = default;
            
            getval(ref this, val1, ref r1);
            getval(ref this, val2, ref r2);
        }

        public void ReadDynIntegerFromMask(out ulong r1, out ulong r2, out ulong r3)
        {
            void getval(ref DataBufferReader data, int mr, ref ulong i)
            {
                if (mr == 0) i = data.ReadValue<byte>();
                if (mr == 1) i = data.ReadValue<ushort>();
                if (mr == 2) i = data.ReadValue<uint>();
                if (mr == 3) i = data.ReadValue<ulong>();
            }

            var mask = ReadValue<byte>();
            var val1 = (mask & 3);
            var val2 = (mask & 12) >> 2;
            var val3 = (mask & 48) >> 4;

            r1 = default;
            r2 = default;
            r3 = default;
            
            getval(ref this, val1, ref r1);
            getval(ref this, val2, ref r2);
            getval(ref this, val3, ref r3);
        }
        
        public void ReadDynIntegerFromMask(out ulong r1, out ulong r2, out ulong r3, out ulong r4)
        {
            void getval(ref DataBufferReader data, int mr, ref ulong i)
            {
                if (mr == 0) i = data.ReadValue<byte>();
                if (mr == 1) i = data.ReadValue<ushort>();
                if (mr == 2) i = data.ReadValue<uint>();
                if (mr == 3) i = data.ReadValue<ulong>();
            }

            var mask = ReadValue<byte>();
            var val1 = (mask & 3);
            var val2 = (mask & 12) >> 2;
            var val3 = (mask & 48) >> 4;
            var val4 = (mask & 192) >> 6;

            r1 = default;
            r2 = default;
            r3 = default;
            r4 = default;
            
            getval(ref this, val1, ref r1);
            getval(ref this, val2, ref r2);
            getval(ref this, val3, ref r3);
            getval(ref this, val3, ref r4);
        }

        public string ReadString(DataBufferMarker marker = default(DataBufferMarker))
        {
            var encoding = (UTF8Encoding) Encoding.UTF8;

            if (!marker.Valid)
                marker = CreateMarker(CurrReadIndex);

            var strDataLength     = ReadValue<int>(marker);
            var strDataEnd        = ReadValue<int>(marker.GetOffset(sizeof(int) * 1));
            var strExpectedLength = ReadValue<int>(marker.GetOffset(sizeof(int) * 2));
            var strDataStart      = GetReadIndex(marker.GetOffset(sizeof(int) * 3));
            
            if (strDataLength <= 0)
            {
                if (strDataLength < 0)
                    Debug.LogWarning("No string found, maybe you are reading at the wrong location or you've done a bad write?");

                return string.Empty;
            }

            var str = encoding.GetString(DataPtr + strDataStart, math.min(strDataEnd - strDataStart, strDataLength));
            CurrReadIndex = strDataEnd;

            if (str.Length != strExpectedLength)
            {
                return str.Substring(0, strExpectedLength);
            }

            return str;
        }
    }
}
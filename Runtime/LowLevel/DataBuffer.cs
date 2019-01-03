using System;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace package.stormiumteam.networking.runtime.lowlevel
{
    public unsafe struct DataBufferMarker
    {
        public int   Index;
        public void* Buffer;

        public DataBufferMarker(void* buffer, int index)
        {
            Index  = index;
            Buffer = buffer;
        }

        public DataBufferMarker GetOffset(int offset)
        {
            return new DataBufferMarker(Buffer, Index + offset);
        }
    }

    public unsafe partial struct DataBufferWriter : IDisposable
    {
        private static object s_ConcurrentLock = new object();
        
        [NativeDisableParallelForRestriction]
        public NativeList<byte> Buffer;
        
        public int Length => Buffer.Length;

        public IntPtr GetSafePtr() => (IntPtr) Buffer.GetUnsafePtr();

        public DataBufferWriter(NativeList<byte> buffer)
        {
            Buffer = buffer;
        }

        public DataBufferWriter(Allocator allocator, int capacity = 0)
        {
            Buffer = new NativeList<byte>(capacity, allocator);
        }

        public void GetWriteInfo(int size, out int writeIndex, DataBufferMarker marker)
        {
            writeIndex = (IntPtr) marker.Buffer == IntPtr.Zero ? Buffer.Length : marker.Index;
            TryResize(writeIndex + size);
        }

        public void TryResize(int maxLength)
        {
            // Needed as we can get `invalid memory pointer` errors in Parallel jobs.
            lock (s_ConcurrentLock)
            {
                Buffer.ResizeUninitialized(math.max(Buffer.Length, maxLength));
            }
        }

        public void WriteData(byte* data, int index, int length)
        {
            Unsafe.CopyBlock((void*) IntPtr.Add((IntPtr) Buffer.GetUnsafePtr(), index), data, (uint) length);
        }

        public DataBufferMarker WriteDataSafe(byte* data, int writeSize, DataBufferMarker marker)
        {
            int writeIndex;

            GetWriteInfo(writeSize, out writeIndex, marker);
            WriteData(data, writeIndex, writeSize);

            return CreateMarker(writeIndex);
        }

        public DataBufferMarker Write<T>(ref T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            lock (s_ConcurrentLock)
            {
                return WriteDataSafe((byte*) Unsafe.AsPointer(ref val), Unsafe.SizeOf<T>(), marker);
            }
        }

        public DataBufferMarker CpyWrite<T>(T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            lock (s_ConcurrentLock)
            {
                return WriteDataSafe((byte*) Unsafe.AsPointer(ref val), Unsafe.SizeOf<T>(), marker);
            }
        }

        public DataBufferMarker CreateMarker(int index)
        {
            return new DataBufferMarker(Unsafe.AsPointer(ref this), index);
        }

        public void Dispose()
        {
            Buffer.Dispose();
        }
    }

    public unsafe partial struct DataBufferWriter
    {
        public DataBufferMarker Write(byte val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(byte), marker);
        }

        public DataBufferMarker Write(short val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(short), marker);
        }

        public DataBufferMarker Write(int val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(int), marker);
        }

        public DataBufferMarker Write(long val, DataBufferMarker marker = default(DataBufferMarker))
        {
            return WriteDataSafe((byte*) &val, sizeof(long), marker);
        }

        public DataBufferMarker Write(string val, Encoding encoding = null, DataBufferMarker marker = default(DataBufferMarker))
        {
            fixed (char* strPtr = val)
            {
                return Write(strPtr, val.Length, encoding, marker);
            }
        }

        public DataBufferMarker Write(char* val, int strLength, Encoding encoding = null, DataBufferMarker marker = default(DataBufferMarker))
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
            if (marker.Buffer != null)
            {
                // Read the data from this buffer
                var reader = new DataBufferReader(Buffer);
                // Start reading from the current marker index.
                var readerMarker = reader.CreateMarker(marker.Index);
                // Get the previous text size from the marker...
                oldCpyLength = reader.ReadValue<int>(readerMarker);
                // Get the difference.
                sizeDiff = math.abs(Buffer.Length - oldCpyLength);
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
                returnMarker = Write(cpyLength, marker);
                // This integer give us the possilibity to know where will be our next values
                // If we update the string with a smaller length, we need to know where our next values are.
                var endMarker = Write(0, returnMarker.GetOffset(sizeof(int)));
                // Write the string buffer data
                Write(strLength, returnMarker.GetOffset(sizeof(int) * 2)); // In future, we should get a better way to define that
                WriteDataSafe((byte*) tempCpyPtr, cpyLength - sizeDiff, returnMarker.GetOffset(sizeof(int) * 3));
                // Re-write the end integer from end marker
                Write(endIndex < 0 ? Buffer.Length : endIndex, endMarker);
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

        public void WriteDynInteger(ulong integer)
        {        
            if (integer <= byte.MaxValue)
            {
                Write((byte) sizeof(byte));
                Write((byte) integer);
            }
            else if (integer <= ushort.MaxValue)
            {
                Write((byte) sizeof(ushort));
                Write((ushort) integer);
            }
            else if (integer <= uint.MaxValue)
            {
                Write((byte) sizeof(uint));
                Write((uint) integer);
            }
            else
            {
                Write((byte) sizeof(ulong));
                Write(ref integer);
            }
        }

        public void WriteStatic(DataBufferWriter dataBuffer)
        {
            WriteDataSafe((byte*) dataBuffer.GetSafePtr(), dataBuffer.Length, default(DataBufferMarker));
        }

        public void WriteStatic(string val, Encoding encoding = null)
        {
            fixed (char* strPtr = val)
            {
                WriteStatic(strPtr, val.Length, encoding);
            }
        }

        public void WriteStatic(char* val, int strLength, Encoding encoding = null)
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
                Write(cpyLength);
                var endMarker = Write(0);
                // Write the string buffer data
                Write(strLength); // In future, we should get a better way to define that
                WriteDataSafe((byte*) tempCpyPtr, cpyLength, default(DataBufferMarker));
                // Re-write the end integer from end marker
                Write(Buffer.Length, endMarker);
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
        public byte* DataPtr;
        public int   CurrReadIndex;
        public int   Length;

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
            var readIndex = marker.Buffer == null ? CurrReadIndex : marker.Index;
            if (readIndex >= Length)
            {
                throw new IndexOutOfRangeException("p1");
            }

            return readIndex;
        }

        public int GetReadIndexAndSetNew(DataBufferMarker marker, int size)
        {
            var readIndex = marker.Buffer == null ? CurrReadIndex : marker.Index;
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
            Unsafe.CopyBlock(data, DataPtr + index, (uint) size);
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
            return new DataBufferMarker(Unsafe.AsPointer(ref this), index);
        }

        public ulong ReadDynInteger(DataBufferMarker marker = default(DataBufferMarker))
        {
            var byteCount = ReadValue<byte>();
            if (byteCount == sizeof(byte)) return ReadValue<byte>();
            if (byteCount == sizeof(ushort)) return ReadValue<ushort>();
            if (byteCount == sizeof(uint)) return ReadValue<uint>();
            if (byteCount == sizeof(ulong)) return ReadValue<ulong>();

            throw new InvalidOperationException();
        }

        public string ReadString(DataBufferMarker marker = default(DataBufferMarker))
        {
            var encoding = (UTF8Encoding) Encoding.UTF8;

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
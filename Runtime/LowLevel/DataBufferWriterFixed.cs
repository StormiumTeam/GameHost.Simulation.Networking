using System;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

namespace package.stormiumteam.networking.runtime.lowlevel
{    
    public unsafe partial struct DataBufferWriterFixed : IDisposable
    {
        private int m_Length;
        private byte* m_BufferPtr;
        
        [WriteOnly]
        private NativeArray<byte> m_Buffer;

        public int Cursor;

        public IntPtr GetSafePtr() => (IntPtr) m_BufferPtr;

        public DataBufferWriterFixed(NativeArray<byte> buffer)
        {
            m_Buffer = buffer;
            m_BufferPtr = (byte*) buffer.GetUnsafePtr();
            m_Length = m_Buffer.Length;
            Cursor = 0;
        }

        public DataBufferWriterFixed(Allocator allocator, int capacity = 0)
        {
            m_Buffer = new NativeArray<byte>(capacity, allocator);
            m_BufferPtr = (byte*) m_Buffer.GetUnsafePtr();
            m_Length = m_Buffer.Length;
            Cursor = 0;
        }

        public void GetWriteInfo(int size, out int writeIndex, DataBufferMarker marker)
        {
            Profiler.BeginSample("Get Write Index");
            writeIndex = !marker.Valid ? Cursor : marker.Index;
            Profiler.EndSample();
            Profiler.BeginSample("Set cursor");
            SetCursor(math.max(writeIndex + size, Cursor));
            Profiler.EndSample();
        }

        public void SetCursor(int position)
        {
            Cursor = position;
            if (Cursor > m_Buffer.Length)
                throw new IndexOutOfRangeException();
        }

        public void AddCursor(int size)
        {
            Cursor += size;
            if (Cursor > m_Buffer.Length)
                throw new IndexOutOfRangeException();
        }

        public void WriteData(byte* data, int index, int length)
        {
            UnsafeUtility.MemCpy((void*) IntPtr.Add((IntPtr) m_BufferPtr, index), data, (uint) length);
        }

        public DataBufferMarker WriteDataSafe(byte* data, int writeSize, DataBufferMarker marker)
        {
            int writeIndex;

            Profiler.BeginSample("Get Write Info()");
            GetWriteInfo(writeSize, out writeIndex, marker);
            Profiler.EndSample();
            Profiler.BeginSample("Write Data()");
            WriteData(data, writeIndex, writeSize);
            Profiler.EndSample();
            
            return CreateMarker(writeIndex);
        }

        public DataBufferMarker Write<T>(ref T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            return WriteDataSafe((byte*) UnsafeUtility.AddressOf(ref val), UnsafeUtility.SizeOf<T>(), marker);
        }

        public DataBufferMarker CpyWrite<T>(T val, DataBufferMarker marker = default(DataBufferMarker))
            where T : struct
        {
            return WriteDataSafe((byte*) UnsafeUtility.AddressOf(ref val), UnsafeUtility.SizeOf<T>(), marker);
        }

        public DataBufferMarker CreateMarker(int index)
        {
            return new DataBufferMarker(index);
        }

        public void Dispose()
        {
            m_Buffer.Dispose();
        }
    }

    public unsafe partial struct DataBufferWriterFixed
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
            if (marker.Valid)
            {
                // Read the data from this buffer
                var reader = new DataBufferReader(m_Buffer);
                // Start reading from the current marker index.
                var readerMarker = reader.CreateMarker(marker.Index);
                // Get the previous text size from the marker...
                oldCpyLength = reader.ReadValue<int>(readerMarker);
                // Get the difference.
                sizeDiff = math.abs(m_Length - oldCpyLength);
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
                Write(endIndex < 0 ? m_Length : endIndex, endMarker);
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
                Write(m_Length, endMarker);
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
}
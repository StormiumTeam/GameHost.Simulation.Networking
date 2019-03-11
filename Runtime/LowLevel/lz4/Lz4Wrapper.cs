using System;
using System.Runtime.InteropServices;

namespace package.stormiumteam.networking.lz4
{
    public unsafe class Lz4Wrapper
    {
        public static int Compress(byte* source, int size, byte* destination)
        {
            if (size == 0)
                throw new Exception();

            return Native.LZ4_compress_default((char*) source, (char*) destination, size, size * 32);
        }

        public static int Decompress(byte* source, byte* destination, int compressedSize, int decompressedSize)
        {
            if (compressedSize == 0)
                throw new Exception("compressedSize=0");
            if (decompressedSize == 0)
                throw new Exception("decompressedSize=0");

            var actualDecompressedSize = Native.LZ4_decompress_safe((char*) source, (char*) destination, compressedSize, decompressedSize);
            return actualDecompressedSize;
        }
        
        private class Native
        {
            private const string nativeLibrary = "liblz4.so.1.8.3";

            [DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int LZ4_compress_default(char* src, char* dst, int srcSize, int dstCapacity);
            
            [DllImport(nativeLibrary, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int LZ4_decompress_safe(char* src, char* dst, int compressedSize, int dstCapacity);
        }
    }
}
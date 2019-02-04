using package.stormiumteam.networking.lz4;
using package.stormiumteam.networking.runtime.lowlevel;
using package.stormiumteam.shared;
using Unity.Collections;
using UnityEngine;

namespace package.stormiumteam.networking.Tests
{
    public unsafe class CompressionTest
    {
        public void Test()
        {
                        // test compression
            using (var sendData = new DataBufferWriter(Allocator.Temp))
            {
                sendData.Write((long) 8);
                sendData.Write((long) 16);
                sendData.Write((long) 32);
                sendData.Write((long) 64);
                sendData.Write((long) 128);
                sendData.Write((long) 256);
                sendData.Write((long) 512);
                sendData.Write((long) 1024);
                sendData.Write((long) 2048);
                sendData.Write((long) 4096);
                sendData.Write((long) 8192);

                using (var compressed = new UnsafeAllocationLength<byte>(Allocator.Temp, sendData.Length))
                {
                    var compressedSize = Lz4Wrapper.Compress((byte*) sendData.GetSafePtr(), sendData.Length, (byte*) compressed.Data);

                    using (var decompressed = new UnsafeAllocationLength<byte>(Allocator.Temp, sendData.Length))
                    {
                        var back = Lz4Wrapper.Decompress((byte*) compressed.Data, (byte*) decompressed.Data, compressedSize, sendData.Length);

                        Debug.Log($"c={compressedSize}, r={back}, l={sendData.Length}");

                        var reader = new DataBufferReader((byte*) decompressed.Data, sendData.Length);
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                        Debug.Log(reader.ReadValue<long>());
                    }
                }
            }
        }
    }
}
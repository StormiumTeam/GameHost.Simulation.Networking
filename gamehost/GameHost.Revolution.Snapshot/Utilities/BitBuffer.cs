using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

// Original Here: https://github.com/nxrighthere/NetStack/blob/master/Source/NetStack.Serialization/BitBuffer.cs
// Differences:
// - Added UIntD4, which serialize every 4 bits (more useful when combined with delta encoding)
// - Added 'Delta' encoding/decoding methods for Int and UIntD4
// - Added AddSpan/ReadSpan
// - Added CopyFrom(BitBuffer)

/*
 *  Copyright (c) 2018 Stanislav Denisov, Maxim Munnig
 *
 *  Permission is hereby granted, free of charge, to any person obtaining a copy
 *  of this software and associated documentation files (the "Software"), to deal
 *  in the Software without restriction, including without limitation the rights
 *  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 *  copies of the Software, and to permit persons to whom the Software is
 *  furnished to do so, subject to the following conditions:
 *
 *  The above copyright notice and this permission notice shall be included in all
 *  copies or substantial portions of the Software.
 *
 *  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 *  IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 *  FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 *  AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *  LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 *  OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 *  SOFTWARE.
 */

/*
 *  Copyright (c) 2018 Alexander Shoulson
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty. In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
 */

namespace GameHost.Revolution.Snapshot.Utilities
{
	public class BitBuffer
	{
		private const int    defaultCapacity  = 375; // 375 * 4 = 1500 bytes
		private const int    stringLengthBits = 8;
		private const int    stringLengthMax  = (1 << stringLengthBits) - 1; // 255
		private const int    bitsASCII        = 7;
		private const int    growFactor       = 2;
		private const int    minGrow          = 1;
		private       uint[] chunks;
		public       int    nextPosition;
		public       int    readPosition;

		public BitBuffer(int capacity = defaultCapacity)
		{
			readPosition = 0;
			nextPosition = 0;
			chunks       = new uint[capacity];
		}

		public int Length => ((nextPosition - 1) >> 3) + 1;

		public bool IsFinished => nextPosition <= readPosition;

		[MethodImpl(256)]
		public void Clear()
		{
			readPosition = 0;
			nextPosition = 0;
		}

		[MethodImpl(256)]
		public BitBuffer Add(int numBits, uint value)
		{
			Debug.Assert(!(numBits < 0));
			Debug.Assert(!(numBits > 32));

			var index = nextPosition >> 5;
			var used  = nextPosition & 0x0000001F;

			if (index + 1 >= chunks.Length)
				ExpandArray();

			var chunkMask = (1UL << used) - 1;
			var scratch   = chunks[index] & chunkMask;
			var result    = scratch | ((ulong) value << used);

			chunks[index]     =  (uint) result;
			chunks[index + 1] =  (uint) (result >> 32);
			nextPosition      += numBits;

			return this;
		}

		[MethodImpl(256)]
		public uint Read(int numBits)
		{
			var result = Peek(numBits);

			readPosition += numBits;

			return result;
		}

		[MethodImpl(256)]
		public uint Peek(int numBits)
		{
			Debug.Assert(!(numBits < 0));
			Debug.Assert(!(numBits > 32));

			var index = readPosition >> 5;
			var used  = readPosition & 0x0000001F;

			var   chunkMask = ((1UL << numBits) - 1) << used;
			ulong scratch   = chunks[index];

			if (index + 1 < chunks.Length)
				scratch |= (ulong) chunks[index + 1] << 32;

			var result = (scratch & chunkMask) >> used;

			return (uint) result;
		}

		public int ToArray(byte[] data)
		{
			Add(1, 1);

			var numChunks = (nextPosition >> 5) + 1;
			var length    = data.Length;

			for (var i = 0; i < numChunks; i++)
			{
				var dataIdx = i * 4;
				var chunk   = chunks[i];

				if (dataIdx < length)
					data[dataIdx] = (byte) chunk;

				if (dataIdx + 1 < length)
					data[dataIdx + 1] = (byte) (chunk >> 8);

				if (dataIdx + 2 < length)
					data[dataIdx + 2] = (byte) (chunk >> 16);

				if (dataIdx + 3 < length)
					data[dataIdx + 3] = (byte) (chunk >> 24);
			}

			return Length;
		}

		public void FromArray(byte[] data, int length)
		{
			var numChunks = length / 4 + 1;

			if (chunks.Length < numChunks)
				chunks = new uint[numChunks];

			for (var i = 0; i < numChunks; i++)
			{
				var  dataIdx = i * 4;
				uint chunk   = 0;

				if (dataIdx < length)
					chunk = data[dataIdx];

				if (dataIdx + 1 < length)
					chunk = chunk | ((uint) data[dataIdx + 1] << 8);

				if (dataIdx + 2 < length)
					chunk = chunk | ((uint) data[dataIdx + 2] << 16);

				if (dataIdx + 3 < length)
					chunk = chunk | ((uint) data[dataIdx + 3] << 24);

				chunks[i] = chunk;
			}

			var positionInByte = FindHighestBitPosition(data[length - 1]);

			nextPosition = (length - 1) * 8 + (positionInByte - 1);
			readPosition = 0;
		}

		public void AddBitBuffer(BitBuffer other)
		{
			other.readPosition = 0;
			
			AddUInt((uint) other.nextPosition);
			while (!other.IsFinished)
			{
				if (other.readPosition + 32 <= other.nextPosition)
					Add(32, other.Read(32));
				if (other.readPosition + 16 <= other.nextPosition)
					Add(16, other.Read(16));
				if (other.readPosition + 8 <= other.nextPosition)
					Add(8, other.Read(8));
				if (other.readPosition + 4 <= other.nextPosition)
					Add(4, other.Read(4));
				if (other.readPosition < other.nextPosition)
					Add(1, other.Read(1));
			}
		}

		public void CopyFrom(BitBuffer other)
		{
			other.readPosition = 0;
			while (!other.IsFinished)
			{
				if (other.readPosition + 32 <= other.nextPosition)
					Add(32, other.Read(32));
				if (other.readPosition + 16 <= other.nextPosition)
					Add(16, other.Read(16));
				if (other.readPosition + 8 <= other.nextPosition)
					Add(8, other.Read(8));
				if (other.readPosition + 4 <= other.nextPosition)
					Add(4, other.Read(4));
				if (other.readPosition < other.nextPosition)
					Add(1, other.Read(1));
			}
		}
		
		public void ReadToExistingBuffer(BitBuffer toFill)
		{
			var next = ReadUInt();
			while (next > 0)
			{
				if (next >= 32)
				{
					toFill.Add(32, Read(32));
					next -= 32;
				}

				if (next >= 16)
				{
					toFill.Add(16, Read(16));
					next -= 16;
				}

				if (next >= 8)
				{
					toFill.Add(8, Read(8));
					next -= 8;
				}

				if (next >= 4)
				{
					toFill.Add(4, Read(4));
					next -= 4;
				}

				if (next > 0)
				{
					toFill.Add(1, Read(1));
					next -= 1;
				}
			}
		}

		public void AddAlign()
		{
			var index = nextPosition >> 5;
			var used  = nextPosition & 0x0000001F;
			
			if (used == 0) // already aligned
				return;
			
			chunks[index + 1] =  0;
			nextPosition      += 32 - used;
		}

		public void ReadAlign()
		{
			var used  = readPosition & 0x0000001F;

			if (used == 0) // already aligned
				return;
			
			readPosition      += 32 - used;
		}

		public unsafe void AddSpan(Span<byte> data)
		{
			AddAlign();
			var index = nextPosition >> 5;
			while (index + data.Length > chunks.Length)
			{
				ExpandArray(index + data.Length);
			}

			if (data.Length == 0)
				return;
			
			fixed (uint* chunkPtr = chunks)
			fixed (byte* dataPtr = data)
			{
				// note for future self. Adding an offset on an uint ptr increase it by 4 bytes, not one (which is logic lol, but can confuse)
				Unsafe.CopyBlock(chunkPtr + index, dataPtr, (uint) data.Length);
				nextPosition += data.Length * 8;
				return;
			}

			for (var i = 0; i != data.Length; i++)
				AddByte(data[i]);
		}

		public void ToSpan(Span<byte> data)
		{
			Clear();
			ReadSpan(data, data.Length);
			readPosition = data.Length * 8;
			nextPosition = data.Length * 8;
		}

		public unsafe void ReadSpan(Span<byte> data, int length)
		{
			if (data.Length < length)
				throw new ArgumentOutOfRangeException();
			
			ReadAlign();
			if (data.Length == 0)
				return;
			
			var index = readPosition >> 5;
			fixed (uint* chunkPtr = chunks)
			fixed (byte* dataPtr = data)
			{
				Unsafe.CopyBlock(dataPtr, chunkPtr + index, (uint) length);
				readPosition += length * 8;
				return;
			}

			return;

			unsafe
			{
				fixed (byte* source = data)
				{
					var ptr = source;
					while (true)
					{
						if (length is { } and >= sizeof(uint))
						{
							Unsafe.Write(ptr, Read(32));
							length -= sizeof(uint);
							ptr    += sizeof(uint);
						}

						if (length is { } and >= sizeof(ushort))
						{
							Unsafe.Write(ptr, Read(16));
							length -= sizeof(ushort);
							ptr    += sizeof(ushort);
						}

						if (length is { } and >= sizeof(byte))
						{
							Unsafe.Write(ptr, Read(8));
							length -= sizeof(byte);
							ptr    += sizeof(byte);
						}
						else
							break;
					}
				}
			}
		}

		[MethodImpl(256)]
		public BitBuffer AddBool(bool value)
		{
			Add(1, value ? 1U : 0U);

			return this;
		}

		[MethodImpl(256)]
		public bool ReadBool()
		{
			return Read(1) > 0;
		}

		[MethodImpl(256)]
		public bool PeekBool()
		{
			return Peek(1) > 0;
		}

		[MethodImpl(256)]
		public BitBuffer AddByte(byte value)
		{
			Add(8, value);

			return this;
		}

		[MethodImpl(256)]
		public byte ReadByte()
		{
			return (byte) Read(8);
		}

		[MethodImpl(256)]
		public byte PeekByte()
		{
			return (byte) Peek(8);
		}

		[MethodImpl(256)]
		public BitBuffer AddShort(short value)
		{
			AddInt(value);

			return this;
		}

		[MethodImpl(256)]
		public short ReadShort()
		{
			return (short) ReadInt();
		}

		[MethodImpl(256)]
		public short PeekShort()
		{
			return (short) PeekInt();
		}

		[MethodImpl(256)]
		public BitBuffer AddUShort(ushort value)
		{
			AddUInt(value);

			return this;
		}

		[MethodImpl(256)]
		public ushort ReadUShort()
		{
			return (ushort) ReadUInt();
		}

		[MethodImpl(256)]
		public ushort PeekUShort()
		{
			return (ushort) PeekUInt();
		}

		[MethodImpl(256)]
		public BitBuffer AddInt(int value)
		{
			var zigzag = (uint) ((value << 1) ^ (value >> 31));

			AddUInt(zigzag);

			return this;
		}

		[MethodImpl(256)]
		public BitBuffer AddIntDelta(int value, int baseline)
		{
			return AddInt(value - baseline);
		}

		[MethodImpl(256)]
		public int ReadInt()
		{
			var value  = ReadUInt();
			var zagzig = (int) ((value >> 1) ^ -(int) (value & 1));

			return zagzig;
		}
		
		[MethodImpl(256)]
		public int ReadIntDelta(int baseline)
		{
			return baseline + ReadInt();
		}

		[MethodImpl(256)]
		public int PeekInt()
		{
			var value  = PeekUInt();
			var zagzig = (int) ((value >> 1) ^ -(int) (value & 1));

			return zagzig;
		}

		[MethodImpl(256)]
		public BitBuffer AddUInt(uint value)
		{
			var buffer = 0x0u;

			do
			{
				buffer =   value & 0x7Fu;
				value  >>= 7;

				if (value > 0)
					buffer |= 0x80u;

				Add(8, buffer);
			} while (value > 0);
			
			return this;
		}
		
		[MethodImpl(256)]
		public BitBuffer AddUIntD4(uint value)
		{
			var buffer = 0x0u;

			do
			{
				buffer =   value & 0x07;
				value  >>= 3;

				if (value > 0)
					buffer |= 0x08;

				Add(4, buffer);
			} while (value > 0);

			return this;
		}

		[MethodImpl(256)]
		public BitBuffer AddUIntD4Delta(uint value, uint baseline)
		{
			return AddUIntD4(value - baseline);
		}

		[MethodImpl(256)]
		public uint ReadUInt()
		{
			var buffer = 0x0u;
			var value  = 0x0u;
			var shift  = 0;

			do
			{
				buffer = Read(8);

				value |= (buffer & 0x7Fu) << shift;
				shift += 7;
			} while ((buffer & 0x80u) > 0);

			return value;
		}
		
		[MethodImpl(256)]
		public uint ReadUIntD4()
		{
			var buffer = 0x0u;
			var value  = 0x0u;
			var shift  = 0;

			do
			{
				buffer = Read(4);

				value |= (buffer & 0x07) << shift;
				shift += 3;
			} while ((buffer & 0x08) > 0);

			return value;
		}

		[MethodImpl(256)]
		public uint ReadUIntD4Delta(uint baseline)
		{
			return baseline + ReadUIntD4();
		}

		[MethodImpl(256)]
		public uint PeekUInt()
		{
			var tempPosition = readPosition;
			var value        = ReadUInt();

			readPosition = tempPosition;

			return value;
		}

		[MethodImpl(256)]
		public BitBuffer AddLong(long value)
		{
			AddInt((int) (value & uint.MaxValue));
			AddInt((int) (value >> 32));

			return this;
		}

		[MethodImpl(256)]
		public long ReadLong()
		{
			var  low   = ReadInt();
			var  high  = ReadInt();
			long value = high;

			return (value << 32) | (uint) low;
		}

		[MethodImpl(256)]
		public long PeekLong()
		{
			var tempPosition = readPosition;
			var value        = ReadLong();

			readPosition = tempPosition;

			return value;
		}

		[MethodImpl(256)]
		public BitBuffer AddULong(ulong value)
		{
			AddUInt((uint) (value & uint.MaxValue));
			AddUInt((uint) (value >> 32));

			return this;
		}

		[MethodImpl(256)]
		public ulong ReadULong()
		{
			var low  = ReadUInt();
			var high = ReadUInt();

			return ((ulong) high << 32) | low;
		}

		[MethodImpl(256)]
		public ulong PeekULong()
		{
			var tempPosition = readPosition;
			var value        = ReadULong();

			readPosition = tempPosition;

			return value;
		}

		[MethodImpl(256)]
		public BitBuffer AddString(string value)
		{
			if (value == null)
				throw new ArgumentNullException("value");

			var length = (uint) value.Length;

			if (length > stringLengthMax)
			{
				length = stringLengthMax;

				throw new ArgumentOutOfRangeException("value length exceeded");
			}

			Add(stringLengthBits, length);

			for (var i = 0; i < length; i++) Add(bitsASCII, ToASCII(value[i]));

			return this;
		}

		[MethodImpl(256)]
		public string ReadString()
		{
			var builder = new StringBuilder();
			var length  = Read(stringLengthBits);

			for (var i = 0; i < length; i++) builder.Append((char) Read(bitsASCII));

			return builder.ToString();
		}

		public override string ToString()
		{
			var builder = new StringBuilder();

			foreach (var chunk in chunks)
			{
				builder.Append(Convert.ToString(chunk, 2).PadLeft(32, '0'));
			}

			var spaced = new StringBuilder();

			for (var i = 0; i < builder.Length; i++)
			{
				spaced.Append(builder[i]);

				if ((i + 1) % 32 == 0)
					spaced.Append(",");
				else if ((i + 1) % 8 == 0)
					spaced.Append(" ");
			}

			return spaced.ToString();
		}

		private void ExpandArray()
		{
			var newCapacity = chunks.Length * growFactor + minGrow;
			var newChunks   = new uint[newCapacity];

			Array.Copy(chunks, newChunks, chunks.Length);
			chunks = newChunks;
		}
		
		private void ExpandArray(int expectedLength)
		{
			var newCapacity = Math.Max(chunks.Length * growFactor + minGrow, expectedLength + minGrow);
			var newChunks   = new uint[newCapacity];

			Array.Copy(chunks, newChunks, chunks.Length);
			chunks = newChunks;
		}

		[MethodImpl(256)]
		private static int FindHighestBitPosition(byte data)
		{
			var shiftCount = 0;

			while (data > 0)
			{
				data >>= 1;
				shiftCount++;
			}

			return shiftCount;
		}

		private static byte ToASCII(char character)
		{
			byte value = 0;

			try
			{
				value = Convert.ToByte(character);
			}

			catch (OverflowException)
			{
				throw new Exception("Cannot convert to ASCII: " + character);
			}

			if (value > 127)
				throw new Exception("Cannot convert to ASCII: " + character);

			return value;
		}
	}
}
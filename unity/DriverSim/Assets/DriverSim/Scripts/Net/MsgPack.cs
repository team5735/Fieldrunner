using System;
using System.Collections.Generic;
using System.Text;

namespace DriverSim.Net
{
    /// <summary>
    /// Minimal MessagePack writer covering what NetworkTables 4 needs: ints, doubles, booleans, strings and arrays.
    /// Carries the bytes written so far.
    /// </summary>
    public sealed class MsgPackWriter
    {
        private readonly List<byte> bytes = new List<byte>(256); // encoded output

        /// <summary>Returns the encoded bytes written so far.</summary>
        public byte[] GetBytes()
        {
            return bytes.ToArray();
        }

        /// <summary>Starts an array; write its elements next.</summary>
        /// <param name="count">Number of elements the array will hold.</param>
        public void WriteArrayHeader(int count)
        {
            if (count < 16)
            {
                bytes.Add((byte)(0x90 | count));
            }
            else if (count < 65536)
            {
                bytes.Add(0xdc);
                WriteBigEndian((ulong)count, 2);
            }
            else
            {
                bytes.Add(0xdd);
                WriteBigEndian((ulong)count, 4);
            }
        }

        /// <summary>Writes a signed integer in the smallest encoding.</summary>
        /// <param name="value">Integer to write.</param>
        public void WriteInt(long value)
        {
            if (value >= 0 && value < 128)
            {
                bytes.Add((byte)value);
            }
            else if (value < 0 && value >= -32)
            {
                bytes.Add((byte)(0xe0 | (value + 32)));
            }
            else
            {
                bytes.Add(0xd3);
                WriteBigEndian((ulong)value, 8);
            }
        }

        /// <summary>Writes a 64-bit float.</summary>
        /// <param name="value">Number to write.</param>
        public void WriteDouble(double value)
        {
            bytes.Add(0xcb);
            long raw = BitConverter.DoubleToInt64Bits(value);
            WriteBigEndian((ulong)raw, 8);
        }

        /// <summary>Writes a boolean.</summary>
        /// <param name="value">Flag to write.</param>
        public void WriteBool(bool value)
        {
            if (value)
            {
                bytes.Add(0xc3);
            }
            else
            {
                bytes.Add(0xc2);
            }
        }

        /// <summary>Writes a UTF-8 string.</summary>
        /// <param name="value">Text to write.</param>
        public void WriteString(string value)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(value);
            if (utf8.Length < 32)
            {
                bytes.Add((byte)(0xa0 | utf8.Length));
            }
            else if (utf8.Length < 256)
            {
                bytes.Add(0xd9);
                bytes.Add((byte)utf8.Length);
            }
            else if (utf8.Length < 65536)
            {
                bytes.Add(0xda);
                WriteBigEndian((ulong)utf8.Length, 2);
            }
            else
            {
                bytes.Add(0xdb);
                WriteBigEndian((ulong)utf8.Length, 4);
            }
            bytes.AddRange(utf8);
        }

        /// <summary>Appends an unsigned value, most significant byte first.</summary>
        /// <param name="value">Value to append.</param>
        /// <param name="byteCount">Number of bytes to use.</param>
        private void WriteBigEndian(ulong value, int byteCount)
        {
            for (int i = byteCount - 1; i >= 0; i--)
            {
                bytes.Add((byte)(value >> (8 * i)));
            }
        }
    }

    /// <summary>
    /// Minimal MessagePack reader. Values come back as boxed long, double, bool, string, byte[], object[] or null.
    /// Carries its read position; use one reader per buffer.
    /// </summary>
    public sealed class MsgPackReader
    {
        private readonly byte[] buffer; // data being decoded
        private readonly int length; // number of valid bytes in the buffer
        private int position; // next byte to read

        /// <summary>Reads from the start of a buffer.</summary>
        /// <param name="buffer">Encoded data.</param>
        /// <param name="length">Number of valid bytes in the buffer.</param>
        public MsgPackReader(byte[] buffer, int length)
        {
            this.buffer = buffer;
            this.length = length;
            this.position = 0;
        }

        /// <summary>Whether unread bytes remain.</summary>
        public bool HasMore()
        {
            return position < length;
        }

        /// <summary>Reads the next value.</summary>
        /// <returns>long, double, bool, string, byte[], object[] or null.</returns>
        /// <exception cref="FormatException">When the data is truncated or uses an unsupported type.</exception>
        public object Read()
        {
            byte tag = ReadByte();

            // fixed-size forms
            if (tag <= 0x7f)
            {
                return (long)tag;
            }
            if (tag >= 0xe0)
            {
                return (long)(sbyte)tag;
            }
            if ((tag & 0xf0) == 0x90)
            {
                return ReadArray(tag & 0x0f);
            }
            if ((tag & 0xf0) == 0x80)
            {
                return ReadMap(tag & 0x0f);
            }
            if ((tag & 0xe0) == 0xa0)
            {
                return ReadUtf8(tag & 0x1f);
            }

            // constants
            if (tag == 0xc0)
            {
                return null;
            }
            if (tag == 0xc2)
            {
                return false;
            }
            if (tag == 0xc3)
            {
                return true;
            }

            // tagged numbers and length-prefixed values
            switch (tag)
            {
                case 0xca: return ReadFloat32();
                case 0xcb: return ReadFloat64();
                case 0xcc: return ReadUnsigned(1);
                case 0xcd: return ReadUnsigned(2);
                case 0xce: return ReadUnsigned(4);
                case 0xcf: return ReadUnsigned(8);
                case 0xd0: return ReadSigned(1);
                case 0xd1: return ReadSigned(2);
                case 0xd2: return ReadSigned(4);
                case 0xd3: return ReadSigned(8);
            }
            int prefixBytes = GetLengthPrefixBytes(tag);
            int count = (int)ReadBigEndian(prefixBytes);
            switch (tag)
            {
                case 0xc4:
                case 0xc5:
                case 0xc6:
                    return ReadBytes(count);
                case 0xd9:
                case 0xda:
                case 0xdb:
                    return ReadUtf8(count);
                case 0xdc:
                case 0xdd:
                    return ReadArray(count);
                default:
                    return ReadMap(count);
            }
        }

        /// <summary>Converts a decoded number (int or float) to double.</summary>
        /// <param name="value">Decoded value.</param>
        /// <returns>The number as a double.</returns>
        /// <exception cref="FormatException">When the value is not a number.</exception>
        public static double ConvertToDouble(object value)
        {
            if (value is double d)
            {
                return d;
            }
            if (value is long l)
            {
                return l;
            }
            throw new FormatException("expected a number");
        }

        // ---- private stateful helpers (each advances the read position)

        private double ReadFloat32()
        {
            int raw = (int)ReadBigEndian(4);
            return BitConverter.Int32BitsToSingle(raw);
        }

        private double ReadFloat64()
        {
            long raw = (long)ReadBigEndian(8);
            return BitConverter.Int64BitsToDouble(raw);
        }

        private long ReadUnsigned(int byteCount)
        {
            ulong raw = ReadBigEndian(byteCount);
            return (long)raw;
        }

        private long ReadSigned(int byteCount)
        {
            ulong raw = ReadBigEndian(byteCount);
            int unusedBits = 64 - 8 * byteCount;
            return ((long)raw << unusedBits) >> unusedBits; // sign-extend
        }

        private object[] ReadArray(int count)
        {
            object[] items = new object[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = Read();
            }
            return items;
        }

        private object ReadMap(int count)
        {
            // NT4 never sends maps in binary frames; read and discard to stay in sync
            for (int i = 0; i < count * 2; i++)
            {
                Read();
            }
            return null;
        }

        private string ReadUtf8(int byteCount)
        {
            CheckAvailable(byteCount);
            string text = Encoding.UTF8.GetString(buffer, position, byteCount);
            position += byteCount;
            return text;
        }

        private byte[] ReadBytes(int byteCount)
        {
            CheckAvailable(byteCount);
            byte[] data = new byte[byteCount];
            Array.Copy(buffer, position, data, 0, byteCount);
            position += byteCount;
            return data;
        }

        private byte ReadByte()
        {
            CheckAvailable(1);
            byte value = buffer[position];
            position++;
            return value;
        }

        private ulong ReadBigEndian(int byteCount)
        {
            CheckAvailable(byteCount);
            ulong value = 0;
            for (int i = 0; i < byteCount; i++)
            {
                value = (value << 8) | buffer[position + i];
            }
            position += byteCount;
            return value;
        }

        private void CheckAvailable(int byteCount)
        {
            if (position + byteCount > length)
            {
                throw new FormatException("truncated MessagePack data");
            }
        }

        // ---- low-level helpers (static, no side effects)

        /// <summary>Size of the length prefix that follows a length-prefixed tag.</summary>
        /// <param name="tag">MessagePack type tag.</param>
        /// <returns>Prefix size in bytes.</returns>
        /// <exception cref="FormatException">When the tag is not a supported length-prefixed type.</exception>
        private static int GetLengthPrefixBytes(byte tag)
        {
            switch (tag)
            {
                case 0xc4:
                case 0xd9:
                    return 1;
                case 0xc5:
                case 0xda:
                case 0xdc:
                case 0xde:
                    return 2;
                case 0xc6:
                case 0xdb:
                case 0xdd:
                case 0xdf:
                    return 4;
                default:
                    throw new FormatException("unsupported MessagePack tag 0x" + tag.ToString("x2"));
            }
        }
    }
}

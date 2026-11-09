using System;
using System.Collections.Generic;
using System.Text;

namespace DriverSim.Net
{
    /// <summary>
    /// Minimal MessagePack writer covering what NetworkTables 4 needs: ints, doubles, booleans, strings, arrays.
    /// </summary>
    public sealed class MsgPackWriter
    {
        private readonly List<byte> bytes = new List<byte>(256); // encoded output

        /// <summary>Encoded bytes so far.</summary>
        public byte[] ToArray()
        {
            return bytes.ToArray();
        }

        /// <summary>Starts an array of <paramref name="count"/> elements; write the elements next.</summary>
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
        public void WriteDouble(double value)
        {
            bytes.Add(0xcb);
            long raw = BitConverter.DoubleToInt64Bits(value);
            WriteBigEndian((ulong)raw, 8);
        }

        /// <summary>Writes a boolean.</summary>
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
    /// Carries its read position; one reader per buffer.
    /// </summary>
    public sealed class MsgPackReader
    {
        private readonly byte[] buffer; // data being decoded
        private int position; // next byte to read

        /// <summary>Reads from a whole buffer.</summary>
        public MsgPackReader(byte[] buffer, int length)
        {
            this.buffer = buffer;
            this.position = 0;
            this.Length = length;
        }

        /// <summary>Number of valid bytes in the buffer.</summary>
        public int Length { get; }

        /// <summary>True while unread bytes remain.</summary>
        public bool HasMore
        {
            get { return position < Length; }
        }

        /// <summary>Reads the next value.</summary>
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

            // tagged forms
            switch (tag)
            {
                case 0xc0: return null;
                case 0xc2: return false;
                case 0xc3: return true;
                case 0xc4: return ReadBytes((int)ReadBigEndian(1));
                case 0xc5: return ReadBytes((int)ReadBigEndian(2));
                case 0xc6: return ReadBytes((int)ReadBigEndian(4));
                case 0xca: return (double)BitConverter.Int32BitsToSingle((int)ReadBigEndian(4));
                case 0xcb: return BitConverter.Int64BitsToDouble((long)ReadBigEndian(8));
                case 0xcc: return (long)ReadBigEndian(1);
                case 0xcd: return (long)ReadBigEndian(2);
                case 0xce: return (long)ReadBigEndian(4);
                case 0xcf: return (long)ReadBigEndian(8);
                case 0xd0: return (long)(sbyte)ReadBigEndian(1);
                case 0xd1: return (long)(short)ReadBigEndian(2);
                case 0xd2: return (long)(int)ReadBigEndian(4);
                case 0xd3: return (long)ReadBigEndian(8);
                case 0xd9: return ReadUtf8((int)ReadBigEndian(1));
                case 0xda: return ReadUtf8((int)ReadBigEndian(2));
                case 0xdb: return ReadUtf8((int)ReadBigEndian(4));
                case 0xdc: return ReadArray((int)ReadBigEndian(2));
                case 0xdd: return ReadArray((int)ReadBigEndian(4));
                case 0xde: return ReadMap((int)ReadBigEndian(2));
                case 0xdf: return ReadMap((int)ReadBigEndian(4));
                default: throw new FormatException("unsupported MessagePack tag 0x" + tag.ToString("x2"));
            }
        }

        /// <summary>Converts a decoded number (int or float) to double.</summary>
        public static double ToDouble(object value)
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
            EnsureAvailable(byteCount);
            string text = Encoding.UTF8.GetString(buffer, position, byteCount);
            position += byteCount;
            return text;
        }

        private byte[] ReadBytes(int byteCount)
        {
            EnsureAvailable(byteCount);
            byte[] data = new byte[byteCount];
            Array.Copy(buffer, position, data, 0, byteCount);
            position += byteCount;
            return data;
        }

        private byte ReadByte()
        {
            EnsureAvailable(1);
            byte value = buffer[position];
            position++;
            return value;
        }

        private ulong ReadBigEndian(int byteCount)
        {
            EnsureAvailable(byteCount);
            ulong value = 0;
            for (int i = 0; i < byteCount; i++)
            {
                value = (value << 8) | buffer[position + i];
            }
            position += byteCount;
            return value;
        }

        private void EnsureAvailable(int byteCount)
        {
            if (position + byteCount > Length)
            {
                throw new FormatException("truncated MessagePack data");
            }
        }
    }
}

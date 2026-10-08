using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Horyzont.Core.Serialization;

/// <summary>
/// Forward-only little-endian reader over untrusted bytes. Every method throws <see cref="ProtocolViolationException"/> (and nothing else)
/// on missing data, a bad value or an exceeded limit.
/// </summary>
public ref struct ByteReader
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    /// <summary>Creates a reader over <paramref name="data"/>.</summary>
    /// <param name="data">Bytes to read.</param>
    public ByteReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    /// <summary>Number of bytes consumed so far.</summary>
    public int Position => _position;

    /// <summary>Number of bytes not yet consumed.</summary>
    public int Remaining => _data.Length - _position;

    /// <summary>Reads one byte.</summary>
    /// <returns>Value.</returns>
    public byte ReadByte()
    {
        if (Remaining < 1)
        {
            throw new ProtocolViolationException("Unexpected end of data reading byte.");
        }

        return _data[_position++];
    }

    /// <summary>Reads one signed byte; -128 is an error.</summary>
    /// <returns>Value.</returns>
    public sbyte ReadSByte()
    {
        byte b = ReadByte();
        if (b == 0x80)
        {
            throw new ProtocolViolationException("SByte value -128 is not permitted.");
        }

        return (sbyte)b;
    }

    /// <summary>Reads a bool; anything but 0 or 1 is an error.</summary>
    /// <returns>Value.</returns>
    public bool ReadBool()
    {
        byte b = ReadByte();
        if (b > 1)
        {
            throw new ProtocolViolationException($"Invalid boolean byte {b}.");
        }

        return b != 0;
    }

    /// <summary>Reads a fixed-width <see cref="ushort"/>.</summary>
    /// <returns>Value.</returns>
    public ushort ReadUInt16()
    {
        if (Remaining < 2)
        {
            throw new ProtocolViolationException("Unexpected end of data reading UInt16.");
        }

        ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_data.Slice(_position, 2));
        _position += 2;
        return v;
    }

    /// <summary>Reads a fixed-width <see cref="uint"/>.</summary>
    /// <returns>Value.</returns>
    public uint ReadUInt32()
    {
        if (Remaining < 4)
        {
            throw new ProtocolViolationException("Unexpected end of data reading UInt32.");
        }

        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
        _position += 4;
        return v;
    }

    /// <summary>Reads a fixed-width <see cref="ulong"/>.</summary>
    /// <returns>Value.</returns>
    public ulong ReadUInt64()
    {
        if (Remaining < 8)
        {
            throw new ProtocolViolationException("Unexpected end of data reading UInt64.");
        }

        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
        _position += 8;
        return v;
    }

    /// <summary>Reads a canonical LEB128 value; overflow past 64 bits and redundant encodings are errors.</summary>
    /// <returns>Value.</returns>
    public ulong ReadVarUInt()
    {
        ulong result = 0;
        int shift = 0;
        int bytesRead = 0;

        while (true)
        {
            if (Remaining < 1)
            {
                throw new ProtocolViolationException("Unexpected end of data reading varuint.");
            }

            byte b = _data[_position++];
            bytesRead++;

            if (bytesRead == 10)
            {
                if (b > 1)
                {
                    throw new ProtocolViolationException("Varuint overflow beyond 64 bits.");
                }

                if (b == 0)
                {
                    throw new ProtocolViolationException("Redundant varuint encoding.");
                }

                result |= (ulong)b << 63;
                break;
            }

            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;

            if ((b & 0x80) == 0)
            {
                if (bytesRead > 1 && b == 0)
                {
                    throw new ProtocolViolationException("Redundant varuint encoding.");
                }

                break;
            }
        }

        return result;
    }

    /// <summary>Reads a zigzag varint.</summary>
    /// <returns>Value.</returns>
    public long ReadVarInt()
    {
        ulong raw = ReadVarUInt();
        return (long)(raw >> 1) ^ -(long)(raw & 1);
    }

    /// <summary>Reads a varuint that must fit in <see cref="uint"/>.</summary>
    /// <returns>Value.</returns>
    public uint ReadVarUInt32()
    {
        ulong v = ReadVarUInt();
        if (v > uint.MaxValue)
        {
            throw new ProtocolViolationException("Varuint value exceeds UInt32.MaxValue.");
        }

        return (uint)v;
    }

    /// <summary>Reads a varuint that must fit in <see cref="ushort"/>.</summary>
    /// <returns>Value.</returns>
    public ushort ReadVarUInt16()
    {
        ulong v = ReadVarUInt();
        if (v > ushort.MaxValue)
        {
            throw new ProtocolViolationException("Varuint value exceeds UInt16.MaxValue.");
        }

        return (ushort)v;
    }

    /// <summary>Reads a varint that must fit in <see cref="int"/>.</summary>
    /// <returns>Value.</returns>
    public int ReadVarInt32()
    {
        long v = ReadVarInt();
        if (v < int.MinValue || v > int.MaxValue)
        {
            throw new ProtocolViolationException("Varint value outside Int32 range.");
        }

        return (int)v;
    }

    /// <summary>Reads a float; NaN and infinities are errors.</summary>
    /// <returns>Value.</returns>
    public float ReadSingle()
    {
        if (Remaining < 4)
        {
            throw new ProtocolViolationException("Unexpected end of data reading float.");
        }

        float v = BinaryPrimitives.ReadSingleLittleEndian(_data.Slice(_position, 4));
        _position += 4;

        if (!float.IsFinite(v))
        {
            throw new ProtocolViolationException("Non-finite float value.");
        }

        return v;
    }

    /// <summary>Reads a half and widens it to float; NaN and infinities are errors.</summary>
    /// <returns>Value.</returns>
    public float ReadHalf()
    {
        if (Remaining < 2)
        {
            throw new ProtocolViolationException("Unexpected end of data reading half.");
        }

        Half h = BinaryPrimitives.ReadHalfLittleEndian(_data.Slice(_position, 2));
        _position += 2;

        if (Half.IsNaN(h) || Half.IsInfinity(h))
        {
            throw new ProtocolViolationException("Non-finite half value.");
        }

        return (float)h;
    }

    /// <summary>Reads three floats.</summary>
    /// <returns>Value.</returns>
    public Vector3 ReadVector3()
    {
        float x = ReadSingle();
        float y = ReadSingle();
        float z = ReadSingle();
        return new Vector3(x, y, z);
    }

    /// <summary>Reads four floats; |length - 1| above 1e-3 is an error.</summary>
    /// <returns>Value.</returns>
    public Quaternion ReadQuaternion()
    {
        float x = ReadSingle();
        float y = ReadSingle();
        float z = ReadSingle();
        float w = ReadSingle();

        var q = new Quaternion(x, y, z, w);
        float len = q.Length();
        if (Math.Abs(len - 1f) > 1e-3f)
        {
            throw new ProtocolViolationException($"Quaternion length {len} is not within 1e-3 of 1.");
        }

        return q;
    }

    /// <summary>Reads 16 bytes as a <see cref="Guid"/>.</summary>
    /// <returns>Value.</returns>
    public Guid ReadGuid()
    {
        if (Remaining < 16)
        {
            throw new ProtocolViolationException("Unexpected end of data reading Guid.");
        }

        var guid = new Guid(_data.Slice(_position, 16));
        _position += 16;
        return guid;
    }

    /// <summary>Reads a length-prefixed strict UTF-8 string of at most <paramref name="maxBytes"/> bytes.</summary>
    /// <param name="maxBytes">Maximum encoded length.</param>
    /// <returns>Value.</returns>
    public string ReadString(int maxBytes)
    {
        ulong len = ReadVarUInt();
        if (len > (ulong)maxBytes)
        {
            throw new ProtocolViolationException($"String byte length {len} exceeds limit {maxBytes}.");
        }

        if ((ulong)Remaining < len)
        {
            throw new ProtocolViolationException("Unexpected end of data reading string.");
        }

        int byteCount = (int)len;
        if (byteCount == 0)
        {
            return string.Empty;
        }

        var span = _data.Slice(_position, byteCount);
        _position += byteCount;

        try
        {
            return _strictUtf8.GetString(span);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or ArgumentException)
        {
            throw new ProtocolViolationException("String contains invalid UTF-8: " + ex.Message);
        }
    }

    /// <summary>Reads a length-prefixed byte array of at most <paramref name="maxBytes"/> bytes into a new array.</summary>
    /// <param name="maxBytes">Maximum length.</param>
    /// <returns>Value.</returns>
    public byte[] ReadBytes(int maxBytes)
    {
        ulong len = ReadVarUInt();
        if (len > (ulong)maxBytes)
        {
            throw new ProtocolViolationException($"Byte array length {len} exceeds limit {maxBytes}.");
        }

        if ((ulong)Remaining < len)
        {
            throw new ProtocolViolationException("Unexpected end of data reading bytes.");
        }

        int byteCount = (int)len;
        if (byteCount == 0)
        {
            return [];
        }

        var array = _data.Slice(_position, byteCount).ToArray();
        _position += byteCount;
        return array;
    }

    /// <summary>Reads a list length (varuint) and checks it against <paramref name="max"/>.</summary>
    /// <param name="max">Maximum count.</param>
    /// <returns>The count.</returns>
    public int ReadCount(int max)
    {
        ulong count = ReadVarUInt();
        if (count > (ulong)max)
        {
            throw new ProtocolViolationException($"Count {count} exceeds limit {max}.");
        }

        return (int)count;
    }

    /// <summary>Throws when unread bytes remain.</summary>
    public void EnsureEnd()
    {
        if (Remaining > 0)
        {
            throw new ProtocolViolationException($"Expected end of data, but {Remaining} bytes remain.");
        }
    }
}

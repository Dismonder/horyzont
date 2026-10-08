using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace Horyzont.Core.Serialization;

/// <summary>Growable little-endian buffer reused across messages; never shrinks. One instance per connection or per writer thread.</summary>
public sealed class ByteWriter
{
    private static readonly UTF8Encoding _strictUtf8 = new(false, true);

    private byte[] _buffer;
    private int _length;

    /// <summary>Creates a writer with the given initial capacity.</summary>
    /// <param name="initialCapacity">Initial buffer size in bytes.</param>
    public ByteWriter(int initialCapacity = 1024)
    {
        _buffer = new byte[Math.Max(1, initialCapacity)];
        _length = 0;
    }

    /// <summary>The bytes written since the last <see cref="Reset"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _length);

    /// <summary>Number of bytes written since the last <see cref="Reset"/>.</summary>
    public int Length => _length;

    /// <summary>Forgets the written bytes; keeps the buffer.</summary>
    public void Reset() => _length = 0;

    /// <summary>Writes one byte.</summary>
    /// <param name="v">Value.</param>
    public void WriteByte(byte v)
    {
        EnsureCapacity(1);
        _buffer[_length++] = v;
    }

    /// <summary>Writes one signed byte.</summary>
    /// <param name="v">Value.</param>
    public void WriteSByte(sbyte v)
    {
        EnsureCapacity(1);
        _buffer[_length++] = (byte)v;
    }

    /// <summary>Writes a bool as 0 or 1.</summary>
    /// <param name="v">Value.</param>
    public void WriteBool(bool v)
    {
        EnsureCapacity(1);
        _buffer[_length++] = v ? (byte)1 : (byte)0;
    }

    /// <summary>Writes a fixed-width little-endian <see cref="ushort"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt16(ushort v)
    {
        EnsureCapacity(2);
        BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length, 2), v);
        _length += 2;
    }

    /// <summary>Writes a fixed-width little-endian <see cref="uint"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt32(uint v)
    {
        EnsureCapacity(4);
        BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length, 4), v);
        _length += 4;
    }

    /// <summary>Writes a fixed-width little-endian <see cref="ulong"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt64(ulong v)
    {
        EnsureCapacity(8);
        BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length, 8), v);
        _length += 8;
    }

    /// <summary>Writes an unsigned LEB128 value (1 to 10 bytes).</summary>
    /// <param name="v">Value.</param>
    public void WriteVarUInt(ulong v)
    {
        EnsureCapacity(10);
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0)
            {
                b |= 0x80;
            }

            _buffer[_length++] = b;
        }
        while (v != 0);
    }

    /// <summary>Writes a zigzag-encoded signed value as a varuint.</summary>
    /// <param name="v">Value.</param>
    public void WriteVarInt(long v) => WriteVarUInt((ulong)((v << 1) ^ (v >> 63)));

    /// <summary>Writes an IEEE 754 float; throws <see cref="ArgumentException"/> for NaN and infinities.</summary>
    /// <param name="v">Value.</param>
    public void WriteSingle(float v)
    {
        if (!float.IsFinite(v))
        {
            throw new ArgumentException("Float value must be finite.", nameof(v));
        }

        EnsureCapacity(4);
        BinaryPrimitives.WriteSingleLittleEndian(_buffer.AsSpan(_length, 4), v);
        _length += 4;
    }

    /// <summary>Writes a float as a 16-bit half; throws <see cref="ArgumentException"/> for NaN, infinities and |v| above 65504.</summary>
    /// <param name="v">Value.</param>
    public void WriteHalf(float v)
    {
        if (!float.IsFinite(v) || Math.Abs(v) > 65504f)
        {
            throw new ArgumentException("Value must be a finite float within half precision range [-65504, 65504].", nameof(v));
        }

        EnsureCapacity(2);
        BinaryPrimitives.WriteHalfLittleEndian(_buffer.AsSpan(_length, 2), (Half)v);
        _length += 2;
    }

    /// <summary>Writes three floats.</summary>
    /// <param name="v">Value.</param>
    public void WriteVector3(Vector3 v)
    {
        WriteSingle(v.X);
        WriteSingle(v.Y);
        WriteSingle(v.Z);
    }

    /// <summary>Writes four floats (X, Y, Z, W); throws <see cref="ArgumentException"/> when |length - 1| exceeds 1e-3.</summary>
    /// <param name="q">Value.</param>
    public void WriteQuaternion(Quaternion q)
    {
        float len = q.Length();
        if (!float.IsFinite(len) || Math.Abs(len - 1f) > 1e-3f)
        {
            throw new ArgumentException("Quaternion length must be within 1e-3 of 1.", nameof(q));
        }

        WriteSingle(q.X);
        WriteSingle(q.Y);
        WriteSingle(q.Z);
        WriteSingle(q.W);
    }

    /// <summary>Writes 16 bytes in the default <see cref="Guid"/> layout.</summary>
    /// <param name="v">Value.</param>
    public void WriteGuid(Guid v)
    {
        EnsureCapacity(16);
        v.TryWriteBytes(_buffer.AsSpan(_length, 16));
        _length += 16;
    }

    /// <summary>Writes a varuint byte length followed by UTF-8; throws <see cref="ArgumentException"/> above <paramref name="maxBytes"/>.</summary>
    /// <param name="v">Value.</param>
    /// <param name="maxBytes">Maximum encoded length.</param>
    public void WriteString(string v, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Maximum bytes cannot be negative.");
        }

        int byteCount;
        try
        {
            byteCount = _strictUtf8.GetByteCount(v);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException("String contains invalid UTF-8 (e.g. lone surrogates).", nameof(v), ex);
        }

        if (byteCount > maxBytes)
        {
            throw new ArgumentException($"Encoded string byte length {byteCount} exceeds limit {maxBytes}.", nameof(v));
        }

        WriteVarUInt((ulong)byteCount);
        EnsureCapacity(byteCount);
        _strictUtf8.GetBytes(v.AsSpan(), _buffer.AsSpan(_length, byteCount));
        _length += byteCount;
    }

    /// <summary>Writes a varuint length followed by the bytes; throws <see cref="ArgumentException"/> above <paramref name="maxBytes"/>.</summary>
    /// <param name="v">Value.</param>
    /// <param name="maxBytes">Maximum length.</param>
    public void WriteBytes(ReadOnlySpan<byte> v, int maxBytes)
    {
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Maximum bytes cannot be negative.");
        }

        if (v.Length > maxBytes)
        {
            throw new ArgumentException($"Byte span length {v.Length} exceeds limit {maxBytes}.", nameof(v));
        }

        WriteVarUInt((ulong)v.Length);
        EnsureCapacity(v.Length);
        v.CopyTo(_buffer.AsSpan(_length, v.Length));
        _length += v.Length;
    }

    private void EnsureCapacity(int additionalBytes)
    {
        int required = _length + additionalBytes;
        if (required > _buffer.Length)
        {
            int newCapacity = Math.Max(_buffer.Length * 2, required);
            Array.Resize(ref _buffer, newCapacity);
        }
    }
}

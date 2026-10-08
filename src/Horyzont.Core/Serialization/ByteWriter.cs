using System.Numerics;

namespace Horyzont.Core.Serialization;

/// <summary>Growable little-endian buffer reused across messages; never shrinks. One instance per connection or per writer thread.</summary>
public sealed class ByteWriter
{
    /// <summary>Creates a writer with the given initial capacity.</summary>
    /// <param name="initialCapacity">Initial buffer size in bytes.</param>
    public ByteWriter(int initialCapacity = 1024) => throw new NotImplementedException();

    /// <summary>The bytes written since the last <see cref="Reset"/>.</summary>
    public ReadOnlySpan<byte> WrittenSpan => throw new NotImplementedException();

    /// <summary>Number of bytes written since the last <see cref="Reset"/>.</summary>
    public int Length => throw new NotImplementedException();

    /// <summary>Forgets the written bytes; keeps the buffer.</summary>
    public void Reset() => throw new NotImplementedException();

    /// <summary>Writes one byte.</summary>
    /// <param name="v">Value.</param>
    public void WriteByte(byte v) => throw new NotImplementedException();

    /// <summary>Writes one signed byte.</summary>
    /// <param name="v">Value.</param>
    public void WriteSByte(sbyte v) => throw new NotImplementedException();

    /// <summary>Writes a bool as 0 or 1.</summary>
    /// <param name="v">Value.</param>
    public void WriteBool(bool v) => throw new NotImplementedException();

    /// <summary>Writes a fixed-width little-endian <see cref="ushort"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt16(ushort v) => throw new NotImplementedException();

    /// <summary>Writes a fixed-width little-endian <see cref="uint"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt32(uint v) => throw new NotImplementedException();

    /// <summary>Writes a fixed-width little-endian <see cref="ulong"/>.</summary>
    /// <param name="v">Value.</param>
    public void WriteUInt64(ulong v) => throw new NotImplementedException();

    /// <summary>Writes an unsigned LEB128 value (1 to 10 bytes).</summary>
    /// <param name="v">Value.</param>
    public void WriteVarUInt(ulong v) => throw new NotImplementedException();

    /// <summary>Writes a zigzag-encoded signed value as a varuint.</summary>
    /// <param name="v">Value.</param>
    public void WriteVarInt(long v) => throw new NotImplementedException();

    /// <summary>Writes an IEEE 754 float; throws <see cref="ArgumentException"/> for NaN and infinities.</summary>
    /// <param name="v">Value.</param>
    public void WriteSingle(float v) => throw new NotImplementedException();

    /// <summary>Writes a float as a 16-bit half; throws <see cref="ArgumentException"/> for NaN, infinities and |v| above 65504.</summary>
    /// <param name="v">Value.</param>
    public void WriteHalf(float v) => throw new NotImplementedException();

    /// <summary>Writes three floats.</summary>
    /// <param name="v">Value.</param>
    public void WriteVector3(Vector3 v) => throw new NotImplementedException();

    /// <summary>Writes four floats (X, Y, Z, W); throws <see cref="ArgumentException"/> when |length - 1| exceeds 1e-3.</summary>
    /// <param name="q">Value.</param>
    public void WriteQuaternion(Quaternion q) => throw new NotImplementedException();

    /// <summary>Writes 16 bytes in the default <see cref="Guid"/> layout.</summary>
    /// <param name="v">Value.</param>
    public void WriteGuid(Guid v) => throw new NotImplementedException();

    /// <summary>Writes a varuint byte length followed by UTF-8; throws <see cref="ArgumentException"/> above <paramref name="maxBytes"/>.</summary>
    /// <param name="v">Value.</param>
    /// <param name="maxBytes">Maximum encoded length.</param>
    public void WriteString(string v, int maxBytes) => throw new NotImplementedException();

    /// <summary>Writes a varuint length followed by the bytes; throws <see cref="ArgumentException"/> above <paramref name="maxBytes"/>.</summary>
    /// <param name="v">Value.</param>
    /// <param name="maxBytes">Maximum length.</param>
    public void WriteBytes(ReadOnlySpan<byte> v, int maxBytes) => throw new NotImplementedException();
}

using System.Numerics;

namespace Horyzont.Core.Serialization;

/// <summary>
/// Forward-only little-endian reader over untrusted bytes. Every method throws <see cref="ProtocolViolationException"/> (and nothing else)
/// on missing data, a bad value or an exceeded limit.
/// </summary>
public ref struct ByteReader
{
    /// <summary>Creates a reader over <paramref name="data"/>.</summary>
    /// <param name="data">Bytes to read.</param>
    public ByteReader(ReadOnlySpan<byte> data) => throw new NotImplementedException();

    /// <summary>Number of bytes consumed so far.</summary>
    public int Position => throw new NotImplementedException();

    /// <summary>Number of bytes not yet consumed.</summary>
    public int Remaining => throw new NotImplementedException();

    /// <summary>Reads one byte.</summary>
    /// <returns>Value.</returns>
    public byte ReadByte() => throw new NotImplementedException();

    /// <summary>Reads one signed byte; -128 is an error.</summary>
    /// <returns>Value.</returns>
    public sbyte ReadSByte() => throw new NotImplementedException();

    /// <summary>Reads a bool; anything but 0 or 1 is an error.</summary>
    /// <returns>Value.</returns>
    public bool ReadBool() => throw new NotImplementedException();

    /// <summary>Reads a fixed-width <see cref="ushort"/>.</summary>
    /// <returns>Value.</returns>
    public ushort ReadUInt16() => throw new NotImplementedException();

    /// <summary>Reads a fixed-width <see cref="uint"/>.</summary>
    /// <returns>Value.</returns>
    public uint ReadUInt32() => throw new NotImplementedException();

    /// <summary>Reads a fixed-width <see cref="ulong"/>.</summary>
    /// <returns>Value.</returns>
    public ulong ReadUInt64() => throw new NotImplementedException();

    /// <summary>Reads a canonical LEB128 value; overflow past 64 bits and redundant encodings are errors.</summary>
    /// <returns>Value.</returns>
    public ulong ReadVarUInt() => throw new NotImplementedException();

    /// <summary>Reads a zigzag varint.</summary>
    /// <returns>Value.</returns>
    public long ReadVarInt() => throw new NotImplementedException();

    /// <summary>Reads a varuint that must fit in <see cref="uint"/>.</summary>
    /// <returns>Value.</returns>
    public uint ReadVarUInt32() => throw new NotImplementedException();

    /// <summary>Reads a varuint that must fit in <see cref="ushort"/>.</summary>
    /// <returns>Value.</returns>
    public ushort ReadVarUInt16() => throw new NotImplementedException();

    /// <summary>Reads a varint that must fit in <see cref="int"/>.</summary>
    /// <returns>Value.</returns>
    public int ReadVarInt32() => throw new NotImplementedException();

    /// <summary>Reads a float; NaN and infinities are errors.</summary>
    /// <returns>Value.</returns>
    public float ReadSingle() => throw new NotImplementedException();

    /// <summary>Reads a half and widens it to float; NaN and infinities are errors.</summary>
    /// <returns>Value.</returns>
    public float ReadHalf() => throw new NotImplementedException();

    /// <summary>Reads three floats.</summary>
    /// <returns>Value.</returns>
    public Vector3 ReadVector3() => throw new NotImplementedException();

    /// <summary>Reads four floats; |length - 1| above 1e-3 is an error.</summary>
    /// <returns>Value.</returns>
    public Quaternion ReadQuaternion() => throw new NotImplementedException();

    /// <summary>Reads 16 bytes as a <see cref="Guid"/>.</summary>
    /// <returns>Value.</returns>
    public Guid ReadGuid() => throw new NotImplementedException();

    /// <summary>Reads a length-prefixed strict UTF-8 string of at most <paramref name="maxBytes"/> bytes.</summary>
    /// <param name="maxBytes">Maximum encoded length.</param>
    /// <returns>Value.</returns>
    public string ReadString(int maxBytes) => throw new NotImplementedException();

    /// <summary>Reads a length-prefixed byte array of at most <paramref name="maxBytes"/> bytes into a new array.</summary>
    /// <param name="maxBytes">Maximum length.</param>
    /// <returns>Value.</returns>
    public byte[] ReadBytes(int maxBytes) => throw new NotImplementedException();

    /// <summary>Reads a list length (varuint) and checks it against <paramref name="max"/>.</summary>
    /// <param name="max">Maximum count.</param>
    /// <returns>The count.</returns>
    public int ReadCount(int max) => throw new NotImplementedException();

    /// <summary>Throws when unread bytes remain.</summary>
    public void EnsureEnd() => throw new NotImplementedException();
}

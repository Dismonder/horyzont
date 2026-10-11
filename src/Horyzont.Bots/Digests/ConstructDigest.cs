using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;
using System.Runtime.InteropServices;

using Horyzont.Core.World.Constructs;

namespace Horyzont.Bots.Digests;

/// <summary>
/// XxHash64 of a construct as the server and every mirror hold it: id, parent frame, <c>ParentSeq</c>, mode, pose bit for bit, version
/// and the blocks of every chunk in <see cref="ChunkPos"/> order. Used only in check breaks (it allocates while sorting).
/// </summary>
public static class ConstructDigest
{
    private const int HeaderBytes = 8 + 1 + 8 + 4 + 1 + (3 * 4) + (4 * 4) + 4;

    /// <summary>Hashes one construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The hash.</returns>
    public static ulong HashOf(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        var hash = new XxHash64();
        Span<byte> header = stackalloc byte[HeaderBytes];
        BinaryPrimitives.WriteUInt64LittleEndian(header, construct.Id.Value);
        header[8] = (byte)construct.Parent.Kind;
        BinaryPrimitives.WriteUInt64LittleEndian(header[9..], construct.Parent.Id);
        BinaryPrimitives.WriteUInt32LittleEndian(header[17..], construct.ParentSeq);
        header[21] = (byte)construct.Mode;
        var origin = construct.Origin;
        var rotation = construct.Rotation;
        WriteFloat(header[22..], origin.X);
        WriteFloat(header[26..], origin.Y);
        WriteFloat(header[30..], origin.Z);
        WriteFloat(header[34..], rotation.X);
        WriteFloat(header[38..], rotation.Y);
        WriteFloat(header[42..], rotation.Z);
        WriteFloat(header[46..], rotation.W);
        BinaryPrimitives.WriteUInt32LittleEndian(header[50..], construct.Version);
        hash.Append(header);
        Span<byte> position = stackalloc byte[12];
        foreach (var (chunk, blocks) in construct.Chunks)
        {
            BinaryPrimitives.WriteInt32LittleEndian(position, chunk.X);
            BinaryPrimitives.WriteInt32LittleEndian(position[4..], chunk.Y);
            BinaryPrimitives.WriteInt32LittleEndian(position[8..], chunk.Z);
            hash.Append(position);
            AppendBlocks(hash, blocks.Blocks);
        }

        return hash.GetCurrentHashAsUInt64();
    }

    /// <summary>Makes the dump entry of a construct.</summary>
    /// <param name="construct">The construct.</param>
    /// <returns>The entry.</returns>
    public static DigestEntry Entry(Construct construct)
    {
        ArgumentNullException.ThrowIfNull(construct);
        return new DigestEntry(
            DigestKind.Construct, construct.Id.Value.ToString(CultureInfo.InvariantCulture), construct.Version, HashOf(construct),
            ((long)construct.Id.Value, 0, 0, 0, 0));
    }

    /// <summary>Digest of a set of constructs; the input order does not matter.</summary>
    /// <param name="constructs">The constructs.</param>
    /// <returns>The digest.</returns>
    public static ulong Compute(IEnumerable<Construct> constructs)
    {
        ArgumentNullException.ThrowIfNull(constructs);
        return DigestDump.Combine(constructs.Select(Entry));
    }

    private static void WriteFloat(Span<byte> destination, float value) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, BitConverter.SingleToInt32Bits(value));

    private static void AppendBlocks(XxHash64 hash, ReadOnlySpan<uint> blocks)
    {
        if (BitConverter.IsLittleEndian)
        {
            hash.Append(MemoryMarshal.AsBytes(blocks));
            return;
        }

        Span<byte> cell = stackalloc byte[4];
        foreach (var block in blocks)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(cell, block);
            hash.Append(cell);
        }
    }
}

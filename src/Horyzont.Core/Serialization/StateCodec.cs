using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;

namespace Horyzont.Core.Serialization;

/// <summary>Binary codec for checkpoint records, registry entries, chunk blocks and SQLite blobs.</summary>
public static class StateCodec
{
    /// <summary>Writes a construct header record without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="h">Record.</param>
    public static void WriteConstructHeader(ByteWriter w, ConstructHeaderRecord h) => throw new NotImplementedException();

    /// <summary>Reads a construct header record.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static ConstructHeaderRecord ReadConstructHeader(ref ByteReader r) => throw new NotImplementedException();

    /// <summary>Writes a player record without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="p">Record.</param>
    public static void WritePlayer(ByteWriter w, PlayerRecord p) => throw new NotImplementedException();

    /// <summary>Reads a player record.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static PlayerRecord ReadPlayer(ref ByteReader r) => throw new NotImplementedException();

    /// <summary>Writes planet parameters without a prefix.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="p">Record.</param>
    public static void WritePlanet(ByteWriter w, PlanetParams p) => throw new NotImplementedException();

    /// <summary>Reads planet parameters.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>The record.</returns>
    public static PlanetParams ReadPlanet(ref ByteReader r) => throw new NotImplementedException();

    /// <summary>Writes a registry entry; the id must be in 1..<paramref name="maxId"/>.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="e">Entry.</param>
    /// <param name="maxId">Largest allowed id (2048 for block types, 255 for materials).</param>
    public static void WriteRegistryEntry(ByteWriter w, RegistryEntry e, int maxId) => throw new NotImplementedException();

    /// <summary>Reads a registry entry.</summary>
    /// <param name="r">Reader.</param>
    /// <param name="maxId">Largest allowed id.</param>
    /// <returns>The entry.</returns>
    public static RegistryEntry ReadRegistryEntry(ref ByteReader r, int maxId) => throw new NotImplementedException();

    /// <summary>Writes the 4096 blocks of a chunk as canonical run-length encoding.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="blocks">Exactly 4096 packed block states.</param>
    public static void WriteChunkBlocks(ByteWriter w, ReadOnlySpan<uint> blocks) => throw new NotImplementedException();

    /// <summary>Reads run-length encoded chunk blocks.</summary>
    /// <param name="r">Reader.</param>
    /// <returns>A new array of 4096 packed block states.</returns>
    public static uint[] ReadChunkBlocks(ref ByteReader r) => throw new NotImplementedException();

    /// <summary>Writes a chunk position followed by its run-length encoded blocks.</summary>
    /// <param name="w">Target writer.</param>
    /// <param name="c">Chunk record.</param>
    public static void WriteChunk(ByteWriter w, ConstructChunkRecord c) => throw new NotImplementedException();

    /// <summary>Reads a chunk position and blocks; the key gets <paramref name="construct"/>.</summary>
    /// <param name="r">Reader.</param>
    /// <param name="construct">Owning construct.</param>
    /// <returns>The chunk record.</returns>
    public static ConstructChunkRecord ReadChunk(ref ByteReader r, EntityId construct) => throw new NotImplementedException();

    /// <summary>Encodes a construct header as a SQLite blob.</summary>
    /// <param name="h">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodeConstructHeaderBlob(ConstructHeaderRecord h) => throw new NotImplementedException();

    /// <summary>Decodes a construct header blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static ConstructHeaderRecord DecodeConstructHeaderBlob(ReadOnlySpan<byte> blob) => throw new NotImplementedException();

    /// <summary>Encodes a player record as a SQLite blob.</summary>
    /// <param name="p">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodePlayerBlob(PlayerRecord p) => throw new NotImplementedException();

    /// <summary>Decodes a player blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static PlayerRecord DecodePlayerBlob(ReadOnlySpan<byte> blob) => throw new NotImplementedException();

    /// <summary>Encodes chunk blocks as a SQLite blob; the chunk key lives in table columns.</summary>
    /// <param name="blocks">Exactly 4096 packed block states.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodeChunkBlob(ReadOnlySpan<uint> blocks) => throw new NotImplementedException();

    /// <summary>Decodes a chunk blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>A new array of 4096 packed block states.</returns>
    public static uint[] DecodeChunkBlob(ReadOnlySpan<byte> blob) => throw new NotImplementedException();

    /// <summary>Encodes planet parameters as a SQLite blob.</summary>
    /// <param name="p">Record.</param>
    /// <returns>The blob.</returns>
    public static byte[] EncodePlanetBlob(PlanetParams p) => throw new NotImplementedException();

    /// <summary>Decodes a planet blob.</summary>
    /// <param name="blob">Blob bytes.</param>
    /// <returns>The record.</returns>
    public static PlanetParams DecodePlanetBlob(ReadOnlySpan<byte> blob) => throw new NotImplementedException();
}

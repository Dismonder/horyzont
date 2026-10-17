using FsCheck;
using FsCheck.Fluent;

using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;
using Horyzont.Testing.Generators;

namespace Horyzont.Core.Tests.Serialization;

/// <summary>The table of codecs that the table-driven properties (round trip, stability, truncation, fuzz) run over.</summary>
public static class CodecCases
{
    private delegate T SpanDecode<out T>(ReadOnlySpan<byte> data);

    /// <summary>Owner used for chunk records (a decoded chunk gets its construct from the caller).</summary>
    public static readonly EntityId ChunkOwner = new(77);

    private static readonly CodecCase[] _all = Build();

    /// <summary>Every codec entry point under test.</summary>
    public static IReadOnlyList<CodecCase> All => _all;

    /// <summary>Theory data: the name of every codec.</summary>
    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var c in _all)
            {
                data.Add(c.Name);
            }

            return data;
        }
    }

    /// <summary>Theory data: the names of codecs whose entry point owns the whole buffer and therefore rejects trailing bytes.</summary>
    public static TheoryData<string> NamesRejectingTrailingBytes
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var c in _all.Where(c => c.RejectsTrailingBytes))
            {
                data.Add(c.Name);
            }

            return data;
        }
    }

    /// <summary>Finds a codec by name.</summary>
    /// <param name="name">Name from <see cref="Names"/>.</param>
    /// <returns>The codec.</returns>
    public static CodecCase Get(string name) => _all.Single(c => c.Name == name);

    private static CodecCase[] Build()
    {
        var messageHeaders = Enumerable.Range(1, 20).Select(t => new[] { (byte)t, (byte)1 }).ToArray();
        var message = new CodecCase(
            "Message",
            CodecGen.Message.Select(m => (object)m),
            v => Kit.Encode((Horyzont.Core.Protocol.Messages.Message)v),
            d => MessageCodec.Read(d))
        {
            StableValues = CodecGen.Message.Where(m => !Canon.HasCompressedRotation(m)).Select(m => (object)m),
            Expected = v => Canon.Expected((Horyzont.Core.Protocol.Messages.Message)v),
            CompressedRotation = v => Canon.HasCompressedRotation((Horyzont.Core.Protocol.Messages.Message)v),
            RejectsTrailingBytes = true,
            MaxChunks = ProtocolConstants.MaxChunksPerConstructStatePart,
            Headers = messageHeaders,
        };

        return
        [
            new CodecCase(
                "Operation",
                CodecGen.Operation.Select(o => (object)o),
                v => Kit.Encode((Horyzont.Core.Operations.Operation)v),
                d => Kit.DecodeOperation(d))
            {
                Headers = Enumerable.Range(1, 7).Select(k => new[] { (byte)k, (byte)1 }).ToArray(),
            },
            new CodecCase(
                "Intent",
                CodecGen.Intent.Select(i => (object)i),
                v => Kit.Encode((Horyzont.Core.Intents.Intent)v),
                d => Kit.DecodeIntent(d))
            {
                Headers = Enumerable.Range(1, 3).Select(k => new[] { (byte)k, (byte)1 }).ToArray(),
            },
            message,
            Record(
                "ConstructHeader",
                CodecGen.ConstructHeader,
                StateCodec.WriteConstructHeader,
                (ref ByteReader r) => StateCodec.ReadConstructHeader(ref r)),
            Record("Player", CodecGen.PlayerRecord, StateCodec.WritePlayer, (ref ByteReader r) => StateCodec.ReadPlayer(ref r)),
            Record("Planet", CodecGen.Planet, StateCodec.WritePlanet, (ref ByteReader r) => StateCodec.ReadPlanet(ref r)),
            Record(
                "BlockTypeEntry",
                CodecGen.RegistryEntry(ProtocolConstants.MaxBlockTypes),
                (w, e) => StateCodec.WriteRegistryEntry(w, e, ProtocolConstants.MaxBlockTypes),
                (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, ProtocolConstants.MaxBlockTypes)),
            Record(
                "MaterialEntry",
                CodecGen.RegistryEntry(ProtocolConstants.MaxMaterials),
                (w, e) => StateCodec.WriteRegistryEntry(w, e, ProtocolConstants.MaxMaterials),
                (ref ByteReader r) => StateCodec.ReadRegistryEntry(ref r, ProtocolConstants.MaxMaterials)),
            Record(
                "ChunkBlocks",
                CodecGen.ChunkBlocks,
                (w, b) => StateCodec.WriteChunkBlocks(w, b),
                (ref ByteReader r) => StateCodec.ReadChunkBlocks(ref r)) with { MaxChunks = 1 },
            Record(
                "ChunkRecord",
                CodecGen.ChunkRecord(ChunkOwner),
                StateCodec.WriteChunk,
                (ref ByteReader r) => StateCodec.ReadChunk(ref r, ChunkOwner)) with { MaxChunks = 1 },
            Blob(
                "ConstructHeaderBlob",
                BlobKind.ConstructHeader,
                CodecGen.ConstructHeader,
                StateCodec.EncodeConstructHeaderBlob,
                d => StateCodec.DecodeConstructHeaderBlob(d)),
            Blob("PlayerBlob", BlobKind.Player, CodecGen.PlayerRecord, StateCodec.EncodePlayerBlob, d => StateCodec.DecodePlayerBlob(d)),
            Blob("PlanetBlob", BlobKind.PlanetParams, CodecGen.Planet, StateCodec.EncodePlanetBlob, d => StateCodec.DecodePlanetBlob(d)),
            Blob(
                "ChunkBlob",
                BlobKind.ConstructChunk,
                CodecGen.ChunkBlocks,
                b => StateCodec.EncodeChunkBlob(b),
                d => StateCodec.DecodeChunkBlob(d)) with { MaxChunks = 1 },
            Blob(
                "ContainerBlob",
                BlobKind.Container,
                CodecGen.ContainerRecord,
                StateCodec.EncodeContainerBlob,
                d => StateCodec.DecodeContainerBlob(d)),
            Blob(
                "TerrainBlockBlob",
                BlobKind.TerrainBlock,
                CodecGen.TerrainBlockBlob,
                b => StateCodec.EncodeTerrainBlockBlob(b.VoxelFormat, b.Voxels),
                d => StateCodec.DecodeTerrainBlockBlob(d)),
        ];
    }

    private static CodecCase Record<T>(string name, Gen<T> gen, Action<ByteWriter, T> write, ReadFn<T> read)
        where T : notnull =>
        new(
            name,
            gen.Select(v => (object)v),
            v => Kit.Write(w => write(w, (T)v)),
            d => Kit.ReadAll(d, read));

    private static CodecCase Blob<T>(string name, BlobKind kind, Gen<T> gen, Func<T, byte[]> encode, SpanDecode<T> decode)
        where T : notnull =>
        new(
            name,
            gen.Select(v => (object)v),
            v => encode((T)v),
            d => decode(d))
        {
            RejectsTrailingBytes = true,
            Headers = [[(byte)kind, 1]],
        };
}

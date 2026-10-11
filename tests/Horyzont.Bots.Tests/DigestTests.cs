using System.Numerics;

using Horyzont.Bots.Digests;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Replication;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Bots.Tests;

public sealed class DigestTests
{
    private static readonly PlanetId _planet = new(1);

    [Fact]
    public void ConstructDigest_SameContent_SameHash_AnyChunkLoadOrder()
    {
        var a = Replica(7, version: 3, chunks: [new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0)]);
        var b = Replica(7, version: 3, chunks: [new ChunkPos(1, 0, 0), new ChunkPos(0, 0, 0)]);

        Assert.Equal(ConstructDigest.HashOf(a.Value), ConstructDigest.HashOf(b.Value));
        Assert.Equal(ConstructDigest.Compute([a.Value]), ConstructDigest.Compute([b.Value]));
    }

    [Fact]
    public void ConstructDigest_EveryFieldMatters()
    {
        var baseline = ConstructDigest.HashOf(Replica(7, version: 3).Value);

        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(8, version: 3).Value));
        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(7, version: 4).Value));
        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(7, version: 3, parentSeq: 1).Value));
        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(7, version: 3, origin: new Vector3(0f, 0f, 1e-6f)).Value));
        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(7, version: 3, block: 3u).Value));
        Assert.NotEqual(baseline, ConstructDigest.HashOf(Replica(7, version: 3, chunks: [new ChunkPos(0, 1, 0)]).Value));
    }

    [Fact]
    public void ConstructDigest_Compute_IgnoresInputOrder_ButNotMembership()
    {
        var a = Replica(1, version: 1).Value;
        var b = Replica(2, version: 5).Value;

        Assert.Equal(ConstructDigest.Compute([a, b]), ConstructDigest.Compute([b, a]));
        Assert.NotEqual(ConstructDigest.Compute([a, b]), ConstructDigest.Compute([a]));
    }

    [Fact]
    public void TerrainVersionDigest_IgnoresOrder_DependsOnKeyAndVersion()
    {
        var k1 = new TerrainBlockKey(_planet, 0, 1, 2, 3);
        var k2 = new TerrainBlockKey(_planet, 0, -4, 5, 6);
        var baseline = TerrainVersionDigest.Compute([new(k1, 1), new(k2, 2)]);

        Assert.Equal(baseline, TerrainVersionDigest.Compute([new(k2, 2), new(k1, 1)]));
        Assert.NotEqual(baseline, TerrainVersionDigest.Compute([new(k2, 3), new(k1, 1)]));
        Assert.NotEqual(baseline, TerrainVersionDigest.Compute([new(k1, 1)]));
        Assert.Throws<ArgumentException>(() => TerrainVersionDigest.Compute([new(k1, 1), new(k1, 2)]));
    }

    [Fact]
    public void Dump_WritesSortedJsonLines_AndReadsThemBack()
    {
        var key = new TerrainBlockKey(_planet, 0, -1, 2, 3);
        var entries = new[]
        {
            TerrainVersionDigest.Entry(key, 4, 0xABCDEF0123456789UL),
            ConstructDigest.Entry(Replica(12, version: 2).Value),
            ConstructDigest.Entry(Replica(3, version: 9).Value),
        };
        using var writer = new StringWriter();

        DigestDump.Write(writer, entries);
        var text = writer.ToString();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("{\"kind\":\"construct\",\"key\":\"3\",\"version\":9,\"hash\":\"", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("{\"kind\":\"construct\",\"key\":\"12\",", lines[1], StringComparison.Ordinal);
        Assert.Equal("{\"kind\":\"terrain\",\"key\":\"1/0/-1/2/3\",\"version\":4,\"hash\":\"abcdef0123456789\"}", lines[2]);
        var read = DigestDump.Read(new StringReader(text));
        Assert.Equal(entries.OrderBy(e => e, DigestEntryComparer.Instance), read);
    }

    [Fact]
    public void Dump_VoxelDigest_IsWrittenAndRead_ButNotCombined()
    {
        var key = new TerrainBlockKey(_planet, 0, 1, 1, 1);
        var with = TerrainVersionDigest.Entry(key, 2, 7, voxels: 0x1234UL);
        using var writer = new StringWriter();

        DigestDump.Write(writer, [with]);

        Assert.EndsWith(",\"voxels\":\"0000000000001234\"}\n", writer.ToString(), StringComparison.Ordinal);
        Assert.Equal(with, DigestDump.Read(new StringReader(writer.ToString())).Single());
        Assert.Equal(DigestDump.Combine([with]), DigestDump.Combine([TerrainVersionDigest.Entry(key, 2, 7)]));
    }

    [Fact]
    public void Dump_SameEntriesInAnyOrder_SameText()
    {
        var a = ConstructDigest.Entry(Replica(1, version: 1).Value);
        var b = TerrainVersionDigest.Entry(new TerrainBlockKey(_planet, 0, 0, 0, 0), 1, 5);
        using var first = new StringWriter();
        using var second = new StringWriter();

        DigestDump.Write(first, [a, b]);
        DigestDump.Write(second, [b, a]);

        Assert.Equal(first.ToString(), second.ToString());
        Assert.Equal(DigestDump.Combine([a, b]), DigestDump.Combine([b, a]));
        Assert.NotEqual(DigestDump.Combine([a, b]), DigestDump.Combine([a]));
    }

    private static ConstructReplica Replica(
        ulong id, uint version, ChunkPos[]? chunks = null, uint parentSeq = 0, Vector3? origin = null, uint block = 2u)
    {
        var entity = new EntityId(id);
        var header = new ConstructHeaderRecord(
            entity, new FrameRef(FrameKind.Planet, 1), parentSeq, ConstructMode.Static, origin ?? new Vector3(4000f, 0f, 0f),
            Quaternion.Identity, version);
        var records = new List<ConstructChunkRecord>();
        foreach (var chunk in chunks ?? [new ChunkPos(0, 0, 0)])
        {
            var blocks = new uint[ConstructChunk.CellCount];
            blocks[(chunk.X & 7) + 1] = block;
            records.Add(new ConstructChunkRecord(new ConstructChunkKey(entity, chunk), blocks));
        }

        return ConstructReplica.Create(header, records);
    }
}

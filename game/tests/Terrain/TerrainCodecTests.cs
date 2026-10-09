using System.Text.Json;

using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public class TerrainCodecTests
{
    private static readonly PlanetId _planet = new(1);

    private static VoxelBuffer Block(float sdf, int material)
    {
        var block = new VoxelBuffer();
        block.Create(16, 16, 16);
        using var format = TerrainChannels.CreateFormat();
        format.ConfigureBuffer(block);
        block.FillF(sdf, (int)TerrainChannels.Sdf);
        block.Fill((ulong)material, (int)TerrainChannels.Material);
        return block;
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Codec_RoundTrip_SameVoxels()
    {
        var codec = new TerrainBlockCodec();
        using var block = Block(-0.25f, 3);
        block.SetVoxelF(0.75f, 3, 4, 5, (int)TerrainChannels.Sdf);
        block.SetVoxel(2, 15, 0, 7, (int)TerrainChannels.Material);

        var blob = codec.Encode(block);
        using var back = new VoxelBuffer();
        codec.Decode(blob, back);

        AssertThat(blob[0]).IsEqual((byte)BlobKind.TerrainBlock);
        AssertThat(StateCodec.DecodeTerrainBlockBlob(blob).VoxelFormat).IsEqual(TerrainEditSemantics.VoxelFormat);
        AssertThat(codec.Digest(back)).IsEqual(codec.Digest(block));
        AssertThat(blob.Length).IsLess(4096);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Codec_Decode_UnknownFormatOrBadPayload_ThrowsProtocolViolation()
    {
        var codec = new TerrainBlockCodec();
        using var block = Block(1f, 0);
        var voxels = VoxelBlockSerializer.SerializeToByteArray(block, TerrainEditSemantics.BlobCompression);
        using var into = new VoxelBuffer();

        AssertThrown(() => codec.Decode(StateCodec.EncodeTerrainBlockBlob(2, voxels), into)).IsInstanceOf<ProtocolViolationException>();
        AssertThrown(() => codec.Decode(StateCodec.EncodeTerrainBlockBlob(1, [1, 2, 3, 4, 5, 6, 7]), into)).IsInstanceOf<ProtocolViolationException>();
        AssertThrown(() => codec.Decode([9, 9, 9], into)).IsInstanceOf<ProtocolViolationException>();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Codec_Digest_IgnoresUniformCompression()
    {
        var codec = new TerrainBlockCodec();
        using var uniform = Block(0.5f, 1);
        using var expanded = Block(0.5f, 1);
        expanded.DecompressChannel((int)TerrainChannels.Sdf);
        expanded.DecompressChannel((int)TerrainChannels.Material);

        AssertThat(expanded.GetChannelCompression((int)TerrainChannels.Sdf)).IsEqual(VoxelBuffer.Compression.None);
        AssertThat(codec.Digest(expanded)).IsEqual(codec.Digest(uniform));
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TerrainDigest_Sensitivity()
    {
        var codec = new TerrainBlockCodec();
        using var block = Block(-0.5f, 0);
        var voxels = codec.Digest(block);
        block.SetVoxelF(-0.25f, 8, 8, 8, (int)TerrainChannels.Sdf);
        var oneVoxel = codec.Digest(block);
        var a = new TerrainBlockKey(_planet, 0, -1, 250, 3);
        var b = new TerrainBlockKey(_planet, 0, 0, 250, 3);
        var c = new TerrainBlockKey(_planet, 0, 5, 249, -7);

        var baseline = TerrainDigest.Compute([(a, 1u, voxels), (c, 4u, 99UL)]);

        AssertThat(oneVoxel).IsNotEqual(voxels);
        AssertThat(TerrainDigest.Compute([(a, 1u, oneVoxel), (c, 4u, 99UL)])).IsNotEqual(baseline);
        AssertThat(TerrainDigest.Compute([(a, 2u, voxels), (c, 4u, 99UL)])).IsNotEqual(baseline);
        AssertThat(TerrainDigest.Compute([(b, 1u, voxels), (c, 4u, 99UL)])).IsNotEqual(baseline);
        AssertThat(TerrainDigest.Compute([(c, 4u, 99UL), (a, 1u, voxels)])).IsEqual(baseline);
        AssertThrown(() => TerrainDigest.Compute([(a, 1u, voxels), (a, 2u, voxels)])).IsInstanceOf<ArgumentException>();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Semantics_VoxelRef_MatchesEngineJsonAndRuntime()
    {
        var path = ProjectSettings.GlobalizePath("res://../engine/engine.json");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var voxelRef = json.RootElement.GetProperty("voxel").GetProperty("ref").GetString() ?? string.Empty;

        AssertThat(TerrainEditSemantics.VoxelRef).IsEqual(voxelRef);
        AssertThat($"v{VoxelEngine.GetVersionMajor()}.{VoxelEngine.GetVersionMinor()}").IsEqual(voxelRef);
        AssertThat(TerrainEditSemantics.Current).IsEqual($"{TerrainEditSemantics.SemanticsVersion}|voxel={voxelRef}");
    }

    [TestCase]
    public void DerivedLodCache_DropsOldestBeyondCapacity()
    {
        var cache = new DerivedLodCache(maxBlocks: 3);
        for (var i = 0; i < 5; i++)
        {
            cache.Put(new TerrainBlockKey(_planet, 1, i, 0, 0), [(byte)i]);
        }

        cache.Put(new TerrainBlockKey(_planet, 1, 3, 0, 0), [33]);
        cache.Put(new TerrainBlockKey(_planet, 1, 5, 0, 0), [5]);

        AssertThat(cache.Count).IsEqual(3);
        AssertThat(cache.TryGet(new TerrainBlockKey(_planet, 1, 2, 0, 0), out _)).IsFalse();
        AssertThat(cache.TryGet(new TerrainBlockKey(_planet, 1, 3, 0, 0), out var three)).IsTrue();
        AssertThat(three[0]).IsEqual((byte)33);
        AssertThat(cache.TryGet(new TerrainBlockKey(_planet, 1, 4, 0, 0), out _)).IsTrue();
        AssertThat(cache.TryGet(new TerrainBlockKey(_planet, 1, 5, 0, 0), out _)).IsTrue();
        AssertThrown(() => cache.Put(new TerrainBlockKey(_planet, 0, 0, 0, 0), [1])).IsInstanceOf<ArgumentException>();
    }
}

using GdUnit4;

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet;
using Horyzont.Game.Terrain;
using Horyzont.Persistence.Terrain;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Terrain;

[TestSuite]
public sealed class DerivedLodReplicationTests
{
    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Rebuild_NewerLod0Version_InvalidatesAllAncestors()
    {
        using var codec = new TerrainBlockCodec();
        using var child = Buffer(10);
        var source = new Source(codec.Encode(child));
        var path = Path.Combine(Path.GetTempPath(), "horyzont-lod-" + Guid.NewGuid().ToString("N") + ".db");
        var store = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance);
        var edited = new TerrainBlockKey(new PlanetId(1), 0, -1, -1, -1);
        var builder = new DerivedLodRebuilder(source, _ => codec.Encode(child), codec, store, "profile/semantics1", 3);
        builder.Invalidate(edited);
        builder.Step(2);
        var parent = edited with { Lod = 1 };
        AssertThat(builder.TryLoad(parent, out var blob)).IsTrue();
        using var actual = new VoxelBuffer();
        codec.Decode(blob, actual);
        using var expected = Buffer(10);
        // Native filter is exactly the method used by voxel_data.cpp for live edit propagation.
        child.DownscaleTo(expected, Vector3I.Zero, new Vector3I(16, 16, 16), new Vector3I(8, 8, 8));
        AssertThat(codec.Digest(actual)).IsEqual(codec.Digest(expected));
        source.Version = 2;
        source.Blob = codec.Encode(Buffer(20));
        builder.Invalidate(edited);
        AssertThat(builder.TryLoad(parent, out _)).IsFalse();
        AssertThat(builder.IsDirty(edited with { Lod = 2 })).IsTrue();
        builder.Step(2);
        AssertThat(builder.TryLoad(parent, out var newer)).IsTrue();
        AssertThat(newer.SequenceEqual(blob)).IsFalse();
        AssertThat(await store.CloseAsync()).IsTrue();
        File.Delete(path);
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Rebuild_DerivedLodAfterRestart_PreservesDistantExcavation()
    {
        using var codec = new TerrainBlockCodec();
        using var edited = Buffer(10);
        using var plain = Buffer(30);
        var source = new Source(codec.Encode(edited));
        var path = Path.Combine(Path.GetTempPath(), "horyzont-lod-" + Guid.NewGuid().ToString("N") + ".db");
        // Excavation on the border of blocks with negative coordinates: the ancestors are floor(x / 2^L).
        var key = new TerrainBlockKey(new PlanetId(1), 0, -1, 0, -1);
        var store = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance);
        var first = new DerivedLodRebuilder(source, _ => codec.Encode(plain), codec, store, "profile/semantics1", 3);
        first.Invalidate(key);
        for (var i = 0; i < 4; i++)
        {
            first.Step(2);
        }

        var top = key with { Lod = 3, X = -1, Y = 0, Z = -1 };
        AssertThat(first.TryLoad(top, out var before)).IsTrue();
        AssertThat(await store.CloseAsync()).IsTrue();

        // Restart: the stream may not read the stale planet in the meantime, and the rebuild finds the persisted entries.
        var reopened = new DerivedLodStore(path, new FakeTimeProvider(), NullLogger<DerivedLodStore>.Instance);
        var second = new DerivedLodRebuilder(source, _ => codec.Encode(plain), codec, reopened, "profile/semantics1", 3);
        second.Invalidate(key);
        AssertThat(second.IsDirty(top)).IsTrue();
        AssertThat(second.TryLoad(top, out _)).IsFalse();
        for (var i = 0; i < 4; i++)
        {
            second.Step(2);
        }

        AssertThat(second.TryLoad(top, out var after)).IsTrue();
        AssertThat(after.SequenceEqual(before)).IsTrue();
        AssertThat(await reopened.CloseAsync()).IsTrue();
        File.Delete(path);
    }

    private static VoxelBuffer Buffer(ulong value)
    {
        var result = new VoxelBuffer();
        result.Create(16, 16, 16);
        result.SetChannelDepth((uint)TerrainChannels.Sdf, TerrainChannels.SdfDepth);
        result.SetChannelDepth((uint)TerrainChannels.Material, TerrainChannels.MaterialDepth);
        result.Fill(value, (int)TerrainChannels.Sdf);
        return result;
    }

    private sealed class Source(byte[] blob) : ITerrainBlockSource
    {
        internal uint Version { get; set; } = 1;
        internal byte[] Blob { get; set; } = blob;
        public TerrainBlockLookup TryLoad(TerrainBlockKey key, out uint version, out byte[] data)
        {
            version = Version;
            data = Blob;
            return TerrainBlockLookup.Found;
        }
    }
}

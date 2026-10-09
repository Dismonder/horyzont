using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

public sealed class TerrainSemanticsMetaTests
{
    private const string VoxelRef = "v1.7";

    private static JournalEntry Entry(ulong id, Operation operation) => new(new OpId(id), new Tick(id), null, operation);

    private static TerrainEditOp Dig => new(WorldFactory.HomePlanet, TerrainShape.Sphere, new Vector3(0, 3999, 0), 1f, TerrainEditMode.Remove, 0);

    private static PlayerLeaveOp Leave => new(new PlayerId(1), default, 0, Vector3.Zero, Quaternion.Identity, 0f);

    [Fact]
    public void Format_VersionAndVoxelRef()
    {
        Assert.Equal("1|voxel=v1.7", TerrainSemanticsMeta.Format(1, VoxelRef));
        Assert.Equal("12|voxel=abc123", TerrainSemanticsMeta.Format(12, "abc123"));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerrainSemanticsMeta.Format(0, VoxelRef));
        Assert.Throws<ArgumentException>(() => TerrainSemanticsMeta.Format(1, ""));
    }

    [Fact]
    public void Read_KeyMissing_IsVersionOneWithCurrentVoxelRef()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());

        Assert.Equal("1|voxel=v1.8", TerrainSemanticsMeta.Read(dir.WorldPath, "v1.8"));
        Assert.Null(RawDb.Scalar(dir.WorldPath, "SELECT value FROM meta WHERE key = 'terrain_semantics';"));
    }

    [Fact]
    public void WriteThenRead_ReturnsTheWrittenValue_AndOverwrites()
    {
        using var dir = new TempDirectory();
        WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());

        TerrainSemanticsMeta.Write(dir.WorldPath, TerrainSemanticsMeta.Format(2, VoxelRef));
        Assert.Equal("2|voxel=v1.7", TerrainSemanticsMeta.Read(dir.WorldPath, "v9"));
        TerrainSemanticsMeta.Write(dir.WorldPath, TerrainSemanticsMeta.Format(3, "v2.0"));

        Assert.Equal("3|voxel=v2.0", TerrainSemanticsMeta.Read(dir.WorldPath, VoxelRef));
    }

    [Fact]
    public async Task Write_WhileTheWriterIsOpen_IsSavedAndTheWorldStillLoads()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            h.Join(1);
            await h.FlushAndPump();
            TerrainSemanticsMeta.Write(dir.WorldPath, TerrainSemanticsMeta.Format(1, VoxelRef));
            h.Ticks(1);
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal("1|voxel=v1.7", TerrainSemanticsMeta.Read(dir.WorldPath, "other"));
        var (loaded, _, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Single(loaded.State.Players.Online);
    }

    [Fact]
    public void CheckTail_TerrainEditUnderOtherSemantics_MismatchWithVersionsCountAndFirstOpId()
    {
        var tail = new[] { Entry(41, Leave), Entry(42, Dig), Entry(43, Leave), Entry(44, Dig) };

        var check = TerrainSemanticsMeta.CheckTail("1|voxel=v1.7", "2|voxel=v1.7", tail);

        Assert.True(check.IsMismatch);
        Assert.False(check.ReplayAllowed);
        Assert.Equal(("1|voxel=v1.7", "2|voxel=v1.7"), (check.SavedSemantics, check.CurrentSemantics));
        Assert.Equal((4, new OpId(41)), (check.TailCount, check.FirstTailOpId));
        Assert.Equal((2, new OpId(42)), (check.TerrainEditCount, check.FirstTerrainEditOpId));
        var message = check.Describe();
        Assert.Contains("'1|voxel=v1.7'", message, StringComparison.Ordinal);
        Assert.Contains("'2|voxel=v1.7'", message, StringComparison.Ordinal);
        Assert.Contains("4 operations from operation 41", message, StringComparison.Ordinal);
        Assert.Contains("2 terrain edits, the first is operation 42", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckTail_NoTerrainEdit_ReplayAllowedDespiteOtherSemantics()
    {
        var check = TerrainSemanticsMeta.CheckTail("1|voxel=v1.7", "2|voxel=v1.8", [Entry(7, Leave)]);

        Assert.True(check.ReplayAllowed);
        Assert.Equal(0, check.TerrainEditCount);
        Assert.Equal(OpId.None, check.FirstTerrainEditOpId);
    }

    [Fact]
    public void CheckTail_EmptyTailOrSameSemantics_ReplayAllowed()
    {
        Assert.True(TerrainSemanticsMeta.CheckTail("1|voxel=a", "2|voxel=b", []).ReplayAllowed);
        Assert.True(TerrainSemanticsMeta.CheckTail("2|voxel=b", "2|voxel=b", [Entry(1, Dig)]).ReplayAllowed);
    }
}

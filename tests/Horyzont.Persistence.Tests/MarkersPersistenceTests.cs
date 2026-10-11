using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Markers;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Tests.Support;

namespace Horyzont.Persistence.Tests;

public sealed class MarkersPersistenceTests
{
    private static MarkerSetOp Set(ulong id, PlayerId? owner, string name) =>
        new(new MarkerId(id), owner, new PlanetId(1), new Vector3(id, 4000, -(float)id), name, (MarkerIcon)(id % 8), (MarkerColor)((id + 3) % 8));

    private static void Populate(WorldHarness h)
    {
        var a = h.Join(1);
        var b = h.Join(2);
        for (ulong i = 1; i <= 5; i++)
        {
            h.Sim.ApplyServerOperation(Set(i, null, $"Shared {i}"));
        }

        h.Sim.ApplyServerOperation(Set(6, a, "A one"));
        h.Sim.ApplyServerOperation(Set(7, a, "A two"));
        h.Sim.ApplyServerOperation(Set(8, b, "B one"));
        h.Sim.ApplyServerOperation(Set(9, b, "B two"));
        h.Sim.ApplyServerOperation(Set(10, b, "B three"));
    }

    [Fact]
    public async Task Markers_CheckpointRestart_SameMarkersAndHash()
    {
        using var dir = new TempDirectory();
        ulong hash;
        MarkerRecord[] markers;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            Populate(h);
            Assert.True(await h.CheckpointAndPump());
            // Edits after the checkpoint: one removed, one renamed, one new (journal tail on top of the checkpoint).
            h.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(2)));
            h.Sim.ApplyServerOperation(Set(3, null, "Renamed"));
            h.Sim.ApplyServerOperation(Set(11, null, "Late"));
            await h.FlushAndPump();
            hash = StateHasher.Hash(h.State);
            markers = [.. h.State.Markers.All];
            Assert.Equal(12ul, h.State.MarkerIds.NextValue);
        }

        Assert.Equal(10, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM markers;"));
        Assert.Equal(11, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'next_marker_id';"));

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(markers, sim.State.Markers.All);
        Assert.Equal(12ul, sim.State.MarkerIds.NextValue);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Markers_SecondCheckpoint_DeletesRemovedRows()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            Populate(h);
            Assert.True(await h.CheckpointAndPump());
            h.Sim.ApplyServerOperation(new MarkerRemoveOp(new MarkerId(10)));
            Assert.True(await h.CheckpointAndPump());
        }

        Assert.Equal(9, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM markers;"));
        Assert.Equal(11, RawDb.Long(dir.WorldPath, "SELECT value FROM meta WHERE key = 'next_marker_id';"));
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.Equal(9, sim.State.Markers.Count);
        Assert.Equal(11ul, sim.State.MarkerIds.NextValue);
    }

    [Fact]
    public async Task Markers_CrashBeforeCheckpoint_JournalReplayRestores()
    {
        using var dir = new TempDirectory();
        ulong hash;
        var faults = ScriptedFaults.CrashAt(FaultPoint.BeforeCheckpointCommit);
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, faults))
        {
            h.FaultsExpected = true;
            Populate(h);
            await h.Writer.FlushAsync();
            h.Pump();
            hash = StateHasher.Hash(h.State);
            await h.RequestCheckpoint();
            h.Pump();
            Assert.True(faults.Crashed);
        }

        // The checkpoint transaction never committed: the rows are absent and the journal holds every marker operation.
        Assert.Equal(0, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM markers;"));
        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(10, sim.State.Markers.Count);
        Assert.Equal(hash, StateHasher.Hash(sim.State));
    }

    [Fact]
    public async Task Markers_OldWorldWithoutMetaKey_NextIdIsOne()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            h.Join(1);
            Assert.True(await h.CheckpointAndPump());
        }

        RawDb.Execute(dir.WorldPath, "DELETE FROM meta WHERE key = 'next_marker_id';");

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(1ul, sim.State.MarkerIds.NextValue);
        Assert.Equal(0, sim.State.Markers.Count);
    }

    [Fact]
    public async Task Markers_DamagedBlob_WorldCorrupted()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            Populate(h);
            Assert.True(await h.CheckpointAndPump());
        }

        RawDb.Execute(dir.WorldPath, "UPDATE markers SET data = X'0B0101' WHERE marker_id = 3;");

        Assert.Throws<WorldCorruptedException>(() => WorldHarness.LoadAndReplay(dir.WorldPath));
    }
}

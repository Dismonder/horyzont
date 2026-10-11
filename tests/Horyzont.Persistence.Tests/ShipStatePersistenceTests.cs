using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Tests.Support;

namespace Horyzont.Persistence.Tests;

/// <summary>K5-24 decision 16: <c>ship_state</c> (blob 12) in the checkpoint transaction and in the loader.</summary>
public sealed class ShipStatePersistenceTests
{
    private static EntityId ShipOf(WorldHarness h)
    {
        var p = h.Join(1);
        var construct = h.BuildOnTerrain(p, 2);
        h.State.Constructs.TryGet(construct, out var c);
        h.Sim.ApplyServerOperation(new ShipModeSetOp(construct, ConstructMode.Ship, c!.Parent, c.ParentSeq, c.Origin, c.Rotation));
        return construct;
    }

    private static ShipStateRecord Sample(EntityId ship) =>
        new(ship, 812.5f, new Vector3(1.5f, -2f, 0.25f), new Vector3(0, 0.5f, 0), ShipStateFlags.Assist | ShipStateFlags.GearDown);

    [Fact]
    public async Task ShipState_CheckpointRestart_FuelVelocityFlagsRestored()
    {
        using var dir = new TempDirectory();
        EntityId ship;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            ship = ShipOf(h);
            Assert.True(h.Sim.UpdateShipState(ship, Sample(ship)));
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM ship_state;"));
        }

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.True(sim.State.ShipStates.TryGet(ship, out var loaded));
        Assert.Equal(Sample(ship), loaded);
    }

    [Fact]
    public async Task ShipState_ConstructRemoved_RowDeletedAtCheckpoint()
    {
        using var dir = new TempDirectory();
        await using var h = WorldHarness.CreateNew(dir.WorldPath);
        var ship = ShipOf(h);
        h.Sim.UpdateShipState(ship, Sample(ship));
        Assert.True(await h.CheckpointAndPump());
        h.State.Constructs.TryGet(ship, out var c);

        h.Sim.ApplyServerOperation(new BlockClearOp(ship, c!.BoundsMin));
        Assert.True(await h.CheckpointAndPump());

        Assert.False(h.State.Constructs.TryGet(ship, out _));
        Assert.Equal(0, h.State.ShipStates.Count);
        Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM ship_state;"));
    }

    [Fact]
    public async Task ShipState_FailedCheckpoint_Requeued()
    {
        using var dir = new TempDirectory();
        EntityId ship;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, ScriptedFaults.DiskErrorAt(FaultPoint.CheckpointMidTransaction)))
        {
            h.FaultsExpected = true;
            ship = ShipOf(h);
            h.Sim.UpdateShipState(ship, Sample(ship));
            Assert.False(await h.CheckpointAndPump());
            Assert.Equal(0L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM ship_state;"));

            h.Writer.ClearFault();
            Assert.True(await h.CheckpointAndPump());
            Assert.Equal(1L, RawDb.Long(dir.WorldPath, "SELECT COUNT(*) FROM ship_state;"));
        }

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);
        Assert.True(sim.State.ShipStates.TryGet(ship, out var loaded));
        Assert.Equal(Sample(ship), loaded);
    }

    [Fact]
    public async Task ShipState_RowWithoutConstruct_SkippedOnLoad()
    {
        using var dir = new TempDirectory();
        await using (var h = WorldHarness.CreateNew(dir.WorldPath))
        {
            var ship = ShipOf(h);
            h.Sim.UpdateShipState(ship, Sample(ship));
            Assert.True(await h.CheckpointAndPump());
        }

        var orphan = new EntityId(9999);
        RawDb.Execute(
            dir.WorldPath, "INSERT INTO ship_state (construct_id, data) VALUES ($id, $data);",
            ("$id", 9999L), ("$data", Core.Serialization.StateCodec.EncodeShipStateBlob(Sample(orphan))));

        var (_, sim, _) = WorldHarness.LoadAndReplay(dir.WorldPath);

        Assert.Equal(1, sim.State.ShipStates.Count);
        Assert.False(sim.State.ShipStates.TryGet(orphan, out _));
    }
}

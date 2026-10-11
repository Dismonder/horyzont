using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-24 decisions 11 and 13: the ship state store, <c>UpdateShipState</c> and <see cref="ShipFuelAddOp"/>.</summary>
public sealed class ShipFuelTests
{
    private static readonly EntityId _ship = ShipFixtures.ConstructId;

    /// <summary>An empty world in which the 30-block ship is built by operations and, optionally, switched to the ship mode.</summary>
    internal static SimFixture Ship(bool shipMode = true)
    {
        var f = new SimFixture(state: ShipFixtures.CreateWorld(null));
        foreach (var op in ShipFixtures.BuildOperations(_ship, ShipFixtures.Thirty, new Vector3(0, f.Radius, 0)))
        {
            f.Sim.ApplyServerOperation(op);
        }

        if (shipMode)
        {
            var c = f.State.Constructs.All.Single();
            f.Sim.ApplyServerOperation(new ShipModeSetOp(_ship, ConstructMode.Ship, c.Parent, c.ParentSeq, c.Origin, c.Rotation));
        }

        return f;
    }

    [Fact]
    public void ShipFuelAddOp_Preconditions_Rejected()
    {
        var f = Ship();
        var hash = StateHasher.Hash(f.State);
        var journal = f.Journal.Entries.Count;
        ShipFuelAddOp[] broken =
        [
            new(new EntityId(999), 10f, 100f),
            new(_ship, 0f, 100f),
            new(_ship, -1f, 100f),
            new(_ship, float.NaN, 100f),
            new(_ship, 10f, -1f),
            new(_ship, 10f, float.PositiveInfinity),
        ];
        foreach (var op in broken)
        {
            Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(op));
        }

        var stationary = Ship(shipMode: false);
        Assert.Throws<InvalidOperationException>(() => stationary.Sim.ApplyServerOperation(new ShipFuelAddOp(_ship, 10f, 100f)));
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journal, f.Journal.Entries.Count);
        Assert.Equal(0, f.State.ShipStates.Count);
    }

    [Fact]
    public void ShipFuelAddOp_ClipsToCapacity()
    {
        var f = Ship();

        f.Sim.ApplyServerOperation(new ShipFuelAddOp(_ship, 1500f, 2000f));
        Assert.True(f.State.ShipStates.TryGet(_ship, out var first));
        f.Sim.ApplyServerOperation(new ShipFuelAddOp(_ship, 1500f, 2000f));
        Assert.True(f.State.ShipStates.TryGet(_ship, out var second));

        Assert.Equal(new ShipStateRecord(_ship, 1500f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked | ShipStateFlags.GearDown), first);
        Assert.Equal(2000f, second.Fuel);
        var checkpoint = f.State.CaptureCheckpoint();
        Assert.Equal([second], checkpoint.ShipStatesToWrite);
    }

    [Fact]
    public void ShipFuelAddOp_JournalReplay_SameHash()
    {
        var live = Ship();
        live.Sim.UpdateShipState(_ship, new ShipStateRecord(_ship, 700f, new Vector3(1, 2, 3), Vector3.Zero, ShipStateFlags.Assist));
        live.Sim.ApplyServerOperation(new ShipFuelAddOp(_ship, 2000f, 2000f));
        var fresh = new SimFixture(state: ShipFixtures.CreateWorld(null));

        foreach (var entry in live.Journal.Entries)
        {
            fresh.Sim.Replay(entry);
        }

        Assert.Equal(StateHasher.Hash(live.State), StateHasher.Hash(fresh.State));
        Assert.True(live.State.ShipStates.TryGet(_ship, out var liveFuel));
        Assert.True(fresh.State.ShipStates.TryGet(_ship, out var replayFuel));
        // N16: the full-tank operation gives the same fuel whatever the fuel was when it is replayed.
        Assert.Equal(2000f, liveFuel.Fuel);
        Assert.Equal(2000f, replayFuel.Fuel);
    }

    [Fact]
    public void UpdateShipState_OnlyValidShips_MarksChangesOnly()
    {
        var f = Ship();
        var valid = new ShipStateRecord(_ship, 10f, new Vector3(0, -3, 0), new Vector3(0, 0.1f, 0), ShipStateFlags.Assist);

        Assert.False(f.Sim.UpdateShipState(new EntityId(999), valid with { Construct = new EntityId(999) }));
        Assert.False(f.Sim.UpdateShipState(_ship, valid with { Fuel = -1f }));
        Assert.False(f.Sim.UpdateShipState(_ship, valid with { LinearVelocity = new Vector3(float.NaN, 0, 0) }));
        Assert.False(f.Sim.UpdateShipState(_ship, valid with { Flags = (ShipStateFlags)8 }));
        Assert.False(f.Sim.UpdateShipState(_ship, valid with { Construct = new EntityId(2) }));
        Assert.False(Ship(shipMode: false).Sim.UpdateShipState(_ship, valid));
        Assert.True(f.Sim.UpdateShipState(_ship, valid));

        Assert.Equal([valid], f.State.CaptureCheckpoint().ShipStatesToWrite);
    }

    [Fact]
    public void UpdateShipState_Unchanged_NotMarked()
    {
        var f = Ship();
        var record = new ShipStateRecord(_ship, 10f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked);
        f.Sim.UpdateShipState(_ship, record);
        f.State.CompleteCheckpoint(f.State.CaptureCheckpoint());

        Assert.True(f.Sim.UpdateShipState(_ship, record));

        Assert.Empty(f.State.CaptureCheckpoint().ShipStatesToWrite);
    }

    [Fact]
    public void ShipToStatic_KeepsRecord()
    {
        var f = Ship();
        f.Sim.ApplyServerOperation(new ShipFuelAddOp(_ship, 300f, 2000f));
        var c = f.State.Constructs.All.Single();

        f.Sim.ApplyServerOperation(new ShipModeSetOp(_ship, ConstructMode.Static, c.Parent, c.ParentSeq, c.Origin, c.Rotation));

        Assert.True(f.State.ShipStates.TryGet(_ship, out var record));
        Assert.Equal(300f, record.Fuel);
    }
}

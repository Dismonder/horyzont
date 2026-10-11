using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-24 decisions 14 and 15: <see cref="RefuelIntent"/> and <see cref="RejectReason.ShipNotParked"/>.</summary>
public sealed class RefuelIntentTests
{
    private static readonly EntityId _ship = ShipFixtures.ConstructId;
    private static readonly EntityId _station = new(2);
    private static readonly CellPos _stationCell = new(0, 0, 0);
    private static readonly CellPos _cockpitCell = new(2, 2, 0);

    /// <summary>The 30-block ship in the ship mode at the origin of the planet top and a refuel station block beside it.</summary>
    private static (SimFixture F, PlayerId P) Rig(float stationX = 9f, ConstructMode mode = ConstructMode.Ship, string stationKey = ShipFixtures.RefuelStation)
    {
        var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, new Vector3(0, 4000, 0), mode);
        var f = new SimFixture(state: state, content: ShipFixtures.ContentFor(state));
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            _station, FrameRef.Of(SimFixture.Planet), new Vector3(stationX, f.Radius, 0), Quaternion.Identity, _stationCell,
            ShipFixtures.StateOf(new ShipBlock(stationKey, _stationCell))));
        var player = f.Join();
        f.MovePlayer(player, new Vector3(stationX + 2.5f, f.Radius, -2.5f));
        return (f, player);
    }

    private static RefuelIntent Aim(SimFixture f, PlayerId player, float stationX = 9f)
    {
        var center = new Vector3(stationX + 0.5f, f.Radius + 0.5f, 0.5f);
        var eye = f.Eye(player);
        return new RefuelIntent(_ship, _station, _stationCell, eye, SimFixture.Direction(eye, center));
    }

    private static void AssertRejected(SimFixture f, PlayerId player, Intent intent, RejectReason expected)
    {
        var hash = StateHasher.Hash(f.State);
        var journal = f.Journal.Entries.Count;

        var result = f.Sim.Submit(player, intent);

        Assert.Equal(expected, result.Reason);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journal, f.Journal.Entries.Count);
    }

    private static void SetState(SimFixture f, float fuel, ShipStateFlags flags) =>
        Assert.True(f.Sim.UpdateShipState(_ship, new ShipStateRecord(_ship, fuel, Vector3.Zero, Vector3.Zero, flags)));

    [Fact]
    public void RefuelIntent_Accepted_ResolvesFullTankOp()
    {
        var (f, player) = Rig();
        SetState(f, 120f, ShipStateFlags.Parked | ShipStateFlags.GearDown);

        var result = f.Sim.Submit(player, Aim(f, player));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        Assert.Equal(new ShipFuelAddOp(_ship, 2000f, 2000f), result.Applied!.Operation);
        Assert.True(f.State.ShipStates.TryGet(_ship, out var record));
        Assert.Equal(2000f, record.Fuel);
    }

    [Fact]
    public void RefuelIntent_NotStation_Rejected()
    {
        var (f, player) = Rig(stationKey: ShipFixtures.Hull);

        AssertRejected(f, player, Aim(f, player), RejectReason.InvalidParameters);
        AssertRejected(f, player, Aim(f, player) with { Station = new EntityId(77) }, RejectReason.UnknownConstruct);
        AssertRejected(f, player, Aim(f, player) with { StationCell = new CellPos(0, 1, 0) }, RejectReason.CellEmpty);
    }

    [Fact]
    public void RefuelIntent_ShipNotParked_Rejected39()
    {
        var (f, player) = Rig();
        SetState(f, 120f, ShipStateFlags.Assist);

        AssertRejected(f, player, Aim(f, player), RejectReason.ShipNotParked);
        Assert.Equal(39, (byte)RejectReason.ShipNotParked);
    }

    [Fact]
    public void RefuelIntent_TooFar_OutOfReach()
    {
        var (f, player) = Rig(stationX: 30f);
        SetState(f, 120f, ShipStateFlags.Parked);

        AssertRejected(f, player, Aim(f, player, 30f), RejectReason.OutOfReach);
    }

    [Fact]
    public void RefuelIntent_TankFull_Rejected()
    {
        var (f, player) = Rig();
        SetState(f, 1999.8f, ShipStateFlags.Parked);

        AssertRejected(f, player, Aim(f, player), RejectReason.InvalidParameters);
    }

    [Fact]
    public void RefuelIntent_OutOfReachOrNoLineOfSight()
    {
        var (f, player) = Rig();
        f.MovePlayer(player, new Vector3(25f, f.Radius, -2.5f));
        AssertRejected(f, player, Aim(f, player), RejectReason.OutOfReach);

        var (g, other) = Rig();
        // A wall block between the eye and the station.
        g.Sim.ApplyServerOperation(new BlockSetOp(_station, new CellPos(1, 1, -1), ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Hull, default))));
        g.Sim.ApplyServerOperation(new BlockSetOp(_station, new CellPos(1, 0, -1), ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Hull, default))));
        g.Sim.ApplyServerOperation(new BlockSetOp(_station, new CellPos(1, 2, -1), ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Hull, default))));
        AssertRejected(g, other, Aim(g, other), RejectReason.NoLineOfSight);

        var (h, staticPlayer) = Rig(mode: ConstructMode.Static);
        AssertRejected(h, staticPlayer, Aim(h, staticPlayer), RejectReason.InvalidParameters);
    }

    [Fact]
    public void ShipModeIntent_ToStaticNotParked_RejectedShipNotParked()
    {
        var (f, player) = Rig();
        f.MovePlayer(player, new Vector3(2.5f, f.Radius, -2f));
        var center = new Vector3(0, f.Radius, 0) + new Vector3(_cockpitCell.X + 0.5f, _cockpitCell.Y + 0.5f, _cockpitCell.Z + 0.5f);
        var eye = f.Eye(player);
        var intent = new ShipModeIntent(_ship, _cockpitCell, ConstructMode.Static, eye, SimFixture.Direction(eye, center));
        SetState(f, 100f, ShipStateFlags.Assist | ShipStateFlags.GearDown);

        AssertRejected(f, player, intent, RejectReason.ShipNotParked);

        SetState(f, 100f, ShipStateFlags.Parked | ShipStateFlags.GearDown);
        var accepted = f.Sim.Submit(player, intent);
        Assert.True(accepted.IsAccepted, accepted.Reason.ToString());
        Assert.Equal(ConstructMode.Static, f.State.Constructs.All.Single(c => c.Id == _ship).Mode);
    }
}

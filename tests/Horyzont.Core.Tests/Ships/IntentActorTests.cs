using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-25 decisions 3, 4 and 10 (Y9, N11): intents of a seated actor, the pilot in the cockpit, NotPilot.</summary>
public sealed class IntentActorTests
{
    private static readonly EntityId _ship = ShipFixtures.ConstructId;
    private static readonly CellPos _cockpit = new(2, 2, 0);
    private static readonly CellPos _seat = new(1, 2, 1);
    private static readonly CellPos _nav = new(2, 2, 1);

    /// <summary>Seated eye of the cockpit in the planet frame: feet (2.5, 2.05, 0.5) + 1.2 up, ship origin (0, 4000, 0).</summary>
    private static readonly Vector3 _cockpitEye = new(2.5f, 4003.25f, 0.5f);

    [Fact]
    public void IntentActor_SeatedEyeInPlanetFrame()
    {
        var (f, p) = SeatIntentTests.Rig();
        SeatIntentTests.Sit(f, p, _cockpit);

        // 1.7 m below the seated eye: within the 2 m ray origin tolerance only from the seated eye (the standing formula from the
        // seat feet would put the eye 0.42 m higher, 2.12 m away).
        var origin = _cockpitEye - new Vector3(0f, 1.7f, 0f);
        var result = f.Sim.Submit(p, new SeatEnterIntent(_ship, _nav, origin, SimFixture.Direction(origin, SeatIntentTests.Centre(_nav))));

        Assert.True(result.IsAccepted, result.Reason.ToString());
    }

    [Fact]
    public void IntentActor_DifferentPlanet_WrongPlanet()
    {
        var other = new PlanetParams(new PlanetId(2), new SystemId(1), 8, "core:test/1");
        var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, new Vector3(0, 4000, 0), ConstructMode.Ship, [other]);
        var f = new SimFixture(state: state, content: ShipFixtures.ContentFor(state));
        var p = f.Join();
        SeatIntentTests.Sit(f, p, _cockpit);
        var station = new EntityId(9);
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            station, FrameRef.Of(other.Id), new Vector3(2f, 4003f, 0f), Quaternion.Identity, new CellPos(0, 0, 0),
            ShipFixtures.StateOf(new ShipBlock(ShipFixtures.Seat, new CellPos(0, 0, 0)))));
        var target = new Vector3(2.5f, 4003.5f, 0.5f);

        SeatIntentTests.AssertRejected(
            f, p, new SeatEnterIntent(station, new CellPos(0, 0, 0), _cockpitEye, SimFixture.Direction(_cockpitEye, target)), RejectReason.WrongPlanet);
    }

    [Fact]
    public void IntentActor_ShipInSystemFrame_WrongPlanet()
    {
        var (f, p) = SeatIntentTests.Rig();
        SeatIntentTests.Sit(f, p, _cockpit);
        ShipFixtures.MoveToSystemFrame(f.Sim, new Vector3(0f, 900_000f, 0f));

        Assert.Equal(FrameKind.System, f.State.Constructs.TryGet(_ship, out var ship) ? ship!.Parent.Kind : FrameKind.Planet);
        SeatIntentTests.AssertRejected(
            f, p, new SeatEnterIntent(_ship, _nav, _cockpitEye, SimFixture.Direction(_cockpitEye, SeatIntentTests.Centre(_nav))), RejectReason.WrongPlanet);
    }

    [Fact]
    public void IntentActor_SeatedBlockEdit_InvalidParameters()
    {
        var (f, p) = SeatIntentTests.Rig();
        SeatIntentTests.Sit(f, p, _cockpit);
        var down = new Vector3(2.5f, 4000f, -2f);

        SeatIntentTests.AssertRejected(f, p, f.Dig(p, down), RejectReason.InvalidParameters);
        SeatIntentTests.AssertRejected(f, p, new RemoveBlockIntent(_ship, new CellPos(1, 2, 0)), RejectReason.InvalidParameters);
        SeatIntentTests.AssertRejected(
            f, p, new PlaceBlockIntent(new ConstructFaceTarget(_ship, _cockpit, Face.PosY), 1, 0), RejectReason.InvalidParameters);
    }

    [Fact]
    public void ShipModeIntent_PilotInCockpit_ToStaticAfterLanding_Accepted()
    {
        var (f, p) = SeatIntentTests.Rig();
        SeatIntentTests.Sit(f, p, _cockpit);
        Assert.True(f.Sim.UpdateShipState(_ship, new ShipStateRecord(_ship, 100f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked)));
        var direction = Vector3.Normalize(SeatIntentTests.Centre(_cockpit) - _cockpitEye);

        var result = f.Sim.Submit(p, new ShipModeIntent(_ship, _cockpit, ConstructMode.Static, _cockpitEye, direction));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        f.State.Constructs.TryGet(_ship, out var ship);
        Assert.Equal(ConstructMode.Static, ship!.Mode);
    }

    [Fact]
    public void ShipModeIntent_SeatedNonPilot_RejectedNotPilot()
    {
        var (f, p) = SeatIntentTests.Rig();
        SeatIntentTests.Sit(f, p, _seat);
        var eye = new Vector3(1.5f, 4003.25f, 1.5f);

        SeatIntentTests.AssertRejected(
            f, p, new ShipModeIntent(_ship, _cockpit, ConstructMode.Static, eye, SimFixture.Direction(eye, SeatIntentTests.Centre(_cockpit))),
            RejectReason.NotPilot);
    }

    [Fact]
    public void RefuelIntent_PilotInCockpit_Accepted()
    {
        var (f, p) = SeatIntentTests.Rig();
        var station = f.State.EntityIds.PeekNext();
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            station, FrameRef.Of(SimFixture.Planet), new Vector3(2f, 4003f, -2f), Quaternion.Identity, new CellPos(0, 0, 0),
            ShipFixtures.StateOf(new ShipBlock(ShipFixtures.RefuelStation, new CellPos(0, 0, 0)))));
        SeatIntentTests.Sit(f, p, _cockpit);
        var target = new Vector3(2.5f, 4003.5f, -1.5f);

        var result = f.Sim.Submit(p, new RefuelIntent(_ship, station, new CellPos(0, 0, 0), _cockpitEye, SimFixture.Direction(_cockpitEye, target)));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        Assert.IsType<ShipFuelAddOp>(result.Applied!.Operation);
    }

    [Fact]
    public void ShipStats_ControlCell_SameCockpitAsControlFrame()
    {
        var blocks = ShipFixtures.Thirty.Append(new ShipBlock(ShipFixtures.Cockpit, new CellPos(4, 2, 0), BlockOrientation.WithYaw(BlockOrientation.Identity, 1)))
            .Append(new ShipBlock(ShipFixtures.Cockpit, new CellPos(0, 2, 0), BlockOrientation.WithYaw(BlockOrientation.Identity, 2))).ToArray();
        var state = ShipFixtures.CreateWorld(blocks, default, ConstructMode.Ship);
        state.Constructs.TryGet(_ship, out var ship);

        var stats = ShipStatsCalculator.Compute(ship!, ShipFixtures.ContentFor(state));

        Assert.Equal(new CellPos(0, 2, 0), stats.ControlCell);
        Assert.Equal(BlockOrientation.ToQuaternion(BlockOrientation.WithYaw(BlockOrientation.Identity, 2)), stats.ControlFrame);
        Assert.True(SeatQueries.TryGetPilot(state, _ship, stats, out _) == false);
    }
}

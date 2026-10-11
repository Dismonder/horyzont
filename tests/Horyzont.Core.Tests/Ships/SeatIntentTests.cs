using System.Numerics;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Motion;
using Horyzont.Core.Operations;
using Horyzont.Core.Ships;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Serialization;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Ships;

/// <summary>K5-25 decisions 2 and 5–8: seat intents 11, 12, 16 on the 30-block ship (cockpit, two seats, navigator).</summary>
public sealed class SeatIntentTests
{
    private static readonly EntityId _ship = ShipFixtures.ConstructId;
    private static readonly CellPos _cockpit = new(2, 2, 0);
    private static readonly CellPos _seat = new(1, 2, 1);
    private static readonly CellPos _nav = new(2, 2, 1);
    private static readonly SeatRules _rules = SeatRules.Default;

    /// <summary>The ship (ship mode, parked) standing on the terrain at x = z = 0 and a player in front of its cockpit.</summary>
    internal static (SimFixture F, PlayerId P) Rig(IEnumerable<ShipBlock>? extra = null, float height = 0f, ConstructMode mode = ConstructMode.Ship)
    {
        var blocks = ShipFixtures.Thirty.Concat(extra ?? []).ToArray();
        var state = ShipFixtures.CreateWorld(blocks, new Vector3(0, 4000 + height, 0), mode);
        var f = new SimFixture(state: state, content: ShipFixtures.ContentFor(state));
        var player = f.Join();
        f.MovePlayer(player, new Vector3(2.5f, 4000f, -2f));
        return (f, player);
    }

    internal static Vector3 Centre(CellPos cell, float height = 0f) => new(cell.X + 0.5f, 4000f + height + cell.Y + 0.5f, cell.Z + 0.5f);

    internal static SeatEnterIntent Enter(SimFixture f, PlayerId player, CellPos cell, Vector3? eye = null)
    {
        var from = eye ?? f.Eye(player);
        return new SeatEnterIntent(_ship, cell, from, SimFixture.Direction(from, Centre(cell)));
    }

    /// <summary>Seats a player with a server operation (no reach rules).</summary>
    internal static void Sit(SimFixture f, PlayerId player, CellPos cell)
    {
        var state = f.PlayerOf(player);
        f.State.Constructs.TryGet(_ship, out var ship);
        var orientation = ship!.GetBlock(cell).Orientation;
        f.Sim.ApplyServerOperation(new EntityReparentOp(
            state.Entity, FrameRef.Of(_ship), state.ParentSeq + 1, SeatPose.Feet(cell, orientation, _rules), SeatPose.Rotation(orientation),
            Vector3.Zero, cell));
    }

    internal static void Leave(SimFixture f, PlayerId player)
    {
        var state = f.PlayerOf(player);
        f.Sim.ApplyServerOperation(new PlayerLeaveOp(player, state.Frame, state.ParentSeq, state.Position, state.BodyRotation, 0f));
    }

    internal static void AssertRejected(SimFixture f, PlayerId player, Intent intent, RejectReason expected)
    {
        var hash = StateHasher.Hash(f.State);
        var journal = f.Journal.Entries.Count;

        var result = f.Sim.Submit(player, intent);

        Assert.Equal(expected, result.Reason);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journal, f.Journal.Entries.Count);
    }

    private static void Fly(SimFixture f, Vector3 velocity, Vector3 angular = default) =>
        Assert.True(f.Sim.UpdateShipState(_ship, new ShipStateRecord(_ship, 500f, velocity, angular, ShipStateFlags.None)));

    [Fact]
    public void SeatEnterIntent_FreeSeatInReach_ResolvesReparentWithSeatPose()
    {
        var (f, p) = Rig();
        var entity = f.PlayerOf(p).Entity;

        var result = f.Sim.Submit(p, Enter(f, p, _cockpit));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        var expected = new EntityReparentOp(
            entity, FrameRef.Of(_ship), 1, new Vector3(2.5f, 2.05f, 0.5f), Quaternion.Identity, Vector3.Zero, _cockpit);
        DeepAssert.Equal(expected, result.Applied!.Operation);
        Assert.Equal(_cockpit, f.PlayerOf(p).Seat);
        Assert.Equal(FrameRef.Of(_ship), f.PlayerOf(p).Frame);
    }

    [Fact]
    public void SeatEnterIntent_Occupied_RejectedSeatOccupied()
    {
        var (f, p) = Rig();
        var other = f.Join(2, "other");
        Sit(f, other, _cockpit);

        AssertRejected(f, p, Enter(f, p, _cockpit), RejectReason.SeatOccupied);
    }

    [Fact]
    public void SeatEnterIntent_OfflineOccupant_RejectedSeatOccupied()
    {
        var (f, p) = Rig();
        var other = f.Join(2, "other");
        Sit(f, other, _cockpit);
        Leave(f, other);

        Assert.False(f.PlayerOf(other).IsOnline);
        AssertRejected(f, p, Enter(f, p, _cockpit), RejectReason.SeatOccupied);
    }

    [Fact]
    public void SeatEnterIntent_TooFar_RejectedSeatOutOfReach()
    {
        var (f, p) = Rig();

        AssertRejected(f, p, Enter(f, p, _seat), RejectReason.SeatOutOfReach);
        f.MovePlayer(p, new Vector3(2.5f, 4000f, -2.6f));
        AssertRejected(f, p, Enter(f, p, _cockpit), RejectReason.SeatOutOfReach);
    }

    [Fact]
    public void SeatEnterIntent_NotSeatBlock_InvalidParameters()
    {
        var (f, p) = Rig();

        AssertRejected(f, p, Enter(f, p, new CellPos(1, 2, 0)), RejectReason.InvalidParameters);
        AssertRejected(f, p, Enter(f, p, new CellPos(2, 3, 0)), RejectReason.CellEmpty);
        AssertRejected(f, p, Enter(f, p, _cockpit) with { Construct = new EntityId(77) }, RejectReason.UnknownConstruct);
    }

    [Fact]
    public void SeatEnterIntent_NoLineOfSight()
    {
        var (f, p) = Rig([new ShipBlock(ShipFixtures.Hull, new CellPos(2, 2, -1)), new ShipBlock(ShipFixtures.Hull, new CellPos(2, 1, -1))]);
        f.MovePlayer(p, new Vector3(2.5f, 4000.9f, -1.6f));

        AssertRejected(f, p, Enter(f, p, _cockpit), RejectReason.NoLineOfSight);
    }

    [Fact]
    public void SeatEnterIntent_ShipFlying_RejectedShipNotParked()
    {
        var (f, p) = Rig();
        Fly(f, Vector3.Zero);

        AssertRejected(f, p, Enter(f, p, _cockpit), RejectReason.ShipNotParked);
    }

    [Fact]
    public void SeatEnterIntent_SwitchSeatSameShip_Accepted()
    {
        var (f, p) = Rig();
        Assert.True(f.Sim.Submit(p, Enter(f, p, _cockpit)).IsAccepted);
        var eye = new Vector3(2.5f, 4000f + 3.25f, 0.5f);

        var result = f.Sim.Submit(p, Enter(f, p, _nav, eye));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        Assert.Equal(_nav, f.PlayerOf(p).Seat);
        Assert.Equal(2u, f.PlayerOf(p).ParentSeq);
    }

    [Fact]
    public void SeatExitIntent_Parked_ExitPointFreeAndOutsideTerrain()
    {
        var (f, p) = Rig();
        Sit(f, p, _cockpit);

        var result = f.Sim.Submit(p, new SeatExitIntent(_ship));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        var op = Assert.IsType<EntityReparentOp>(result.Applied!.Operation);
        Assert.Equal(FrameRef.Of(SimFixture.Planet), op.NewParent);
        Assert.Null(op.SeatCell);
        Assert.Equal(Vector3.Zero, op.LocalVelocity);
        Assert.True(Vector3.Distance(new Vector3(2.5f, 4002.05f, -0.5f), op.LocalPosition) < 1e-3f, op.LocalPosition.ToString());
        Assert.True(op.LocalPosition.Length() > f.Radius);
        Assert.Null(f.PlayerOf(p).Seat);
    }

    [Fact]
    public void SeatExitIntent_NoFreeCell_RejectedCellOccupied()
    {
        var (f, p) = Rig([new ShipBlock(ShipFixtures.Hull, new CellPos(2, 2, -1)), new ShipBlock(ShipFixtures.Hull, new CellPos(2, 3, 0))]);
        Sit(f, p, _cockpit);

        AssertRejected(f, p, new SeatExitIntent(_ship), RejectReason.CellOccupied);
    }

    [Fact]
    public void SeatExitIntent_InFlight_Under30And200_EjectsWithShipVelocity()
    {
        var (f, p) = Rig(height: 50f);
        Sit(f, p, _cockpit);
        var v = new Vector3(10f, 0f, 5f);
        var w = new Vector3(0f, 0.1f, 0f);
        Fly(f, v, w);

        var result = f.Sim.Submit(p, new SeatExitIntent(_ship));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        var op = Assert.IsType<EntityReparentOp>(result.Applied!.Operation);
        f.State.Constructs.TryGet(_ship, out var ship);
        var com = ship!.LocalToParent(ShipStatsCalculator.Compute(ship, ShipFixtures.ContentFor(f.State)).CenterOfMass);
        var expected = v + Vector3.Cross(w, op.LocalPosition - com);
        Assert.True(Vector3.Distance(expected, op.LocalVelocity) < 1e-4f, $"{expected} vs {op.LocalVelocity}");
        Assert.Equal(FrameRef.Of(SimFixture.Planet), op.NewParent);
        Assert.True(EjectRules.StartsGlide(op, FrameKind.Construct));
    }

    [Fact]
    public void SeatExitIntent_InFlight_Over30ms_RejectedEjectTooFast()
    {
        var (f, p) = Rig(height: 50f);
        Sit(f, p, _cockpit);
        Fly(f, new Vector3(30.5f, 0f, 0f));

        AssertRejected(f, p, new SeatExitIntent(_ship), RejectReason.EjectTooFast);
    }

    [Fact]
    public void SeatExitIntent_ShipInSystemFrame_RejectedEjectTooFast()
    {
        var (f, p) = Rig();
        Sit(f, p, _cockpit);
        ShipFixtures.MoveToSystemFrame(f.Sim, new Vector3(0f, 900_000f, 0f));

        AssertRejected(f, p, new SeatExitIntent(_ship), RejectReason.EjectTooFast);
    }

    [Fact]
    public void SeatExitIntent_InFlight_Over200m_RejectedEjectTooFast()
    {
        var (f, p) = Rig(height: 205f);
        Sit(f, p, _cockpit);
        Fly(f, new Vector3(1f, 0f, 0f));

        AssertRejected(f, p, new SeatExitIntent(_ship), RejectReason.EjectTooFast);
    }

    [Fact]
    public void SeatExitIntent_NotSeated_InvalidParameters()
    {
        var (f, p) = Rig();

        AssertRejected(f, p, new SeatExitIntent(_ship), RejectReason.InvalidParameters);
        Sit(f, p, _cockpit);
        AssertRejected(f, p, new SeatExitIntent(new EntityId(77)), RejectReason.InvalidParameters);
    }

    [Fact]
    public void SeatEvictIntent_OfflineOccupant_MovedToWorldStart()
    {
        var (f, p) = Rig();
        var other = f.Join(2, "other");
        Sit(f, other, _cockpit);
        Leave(f, other);
        var entity = f.PlayerOf(other).Entity;
        var eye = f.Eye(p);

        var result = f.Sim.Submit(p, new SeatEvictIntent(_ship, _cockpit, eye, SimFixture.Direction(eye, Centre(_cockpit))));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        var start = f.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 99), "new").Position;
        DeepAssert.Equal(
            new EntityReparentOp(entity, FrameRef.Of(SimFixture.Planet), 2, start, Quaternion.Identity, Vector3.Zero, null),
            result.Applied!.Operation);
        Assert.Null(f.PlayerOf(other).Seat);
    }

    [Fact]
    public void SeatEvictIntent_OnlineOccupant_InvalidParameters()
    {
        var (f, p) = Rig();
        var other = f.Join(2, "other");
        Sit(f, other, _cockpit);
        var eye = f.Eye(p);

        AssertRejected(f, p, new SeatEvictIntent(_ship, _cockpit, eye, SimFixture.Direction(eye, Centre(_cockpit))), RejectReason.InvalidParameters);
    }

    [Fact]
    public void SeatEvictIntent_Self_InvalidParameters()
    {
        var (f, p) = Rig();
        Assert.True(f.Sim.Submit(p, Enter(f, p, _cockpit)).IsAccepted);
        var eye = new Vector3(2.5f, 4003.25f, 0.5f);

        AssertRejected(f, p, new SeatEvictIntent(_ship, _cockpit, eye, SimFixture.Direction(eye, Centre(_cockpit))), RejectReason.InvalidParameters);
    }

    [Fact]
    public void SeatEvictIntent_ShipNotParked_Rejected39()
    {
        var (f, p) = Rig();
        var other = f.Join(2, "other");
        Sit(f, other, _cockpit);
        Leave(f, other);
        Fly(f, Vector3.Zero);
        var eye = f.Eye(p);

        AssertRejected(f, p, new SeatEvictIntent(_ship, _cockpit, eye, SimFixture.Direction(eye, Centre(_cockpit))), RejectReason.ShipNotParked);
    }

    [Fact]
    public void RespawnPoints_WorldStart_BitwiseEqualsOldSpawnPoint()
    {
        var f = new SimFixture();
        f.State.Planets.TryGet(f.State.HomePlanet, out var home);

        var join = f.Sim.PreparePlayerJoin(new PlatformIdentity(IdentityPlatform.Dev, 5), "new");

        // The pre-K5-25 formula: terrain under the top of the atmosphere above the pole, lifted 0.1 m.
        var top = home!.RadiusMeters + home.AtmosphereHeightMeters;
        Assert.True(f.Terrain.Raycast(home.Id, new Vector3(0f, top, 0f), -Vector3.UnitY, top, out var hit));
        var old = hit.Position + (TerrainMath.UpAt(hit.Position) * 0.1f);
        Assert.Equal(old, join.Position);
        Assert.Equal(old, RespawnPoints.WorldStart(home, f.Terrain));
    }

    [Fact]
    public void SeatPose_FeetEyeAndRotation_FollowTheBlock()
    {
        var orientation = BlockOrientation.FromUpForward(Face.PosX, Face.NegZ);

        var feet = SeatPose.Feet(new CellPos(0, 0, 0), orientation, _rules);
        var eye = SeatPose.Eye(new CellPos(0, 0, 0), orientation, _rules);

        Assert.Equal(new Vector3(0.05f, 0.5f, 0.5f), feet);
        Assert.Equal(new Vector3(1.25f, 0.5f, 0.5f), eye);
        Assert.Equal(BlockOrientation.ToQuaternion(orientation), SeatPose.Rotation(orientation));
        Assert.Equal(new Vector3(2.5f, 2.05f, 0.5f), SeatPose.Feet(_cockpit, BlockOrientation.Identity, _rules));
    }

    [Fact]
    public void Gliding_StartsOnlyFromConstructWithVelocity()
    {
        var op = new EntityReparentOp(new EntityId(1), FrameRef.Of(SimFixture.Planet), 1, Vector3.Zero, Quaternion.Identity, new Vector3(0.6f, 0, 0), null);

        Assert.True(EjectRules.StartsGlide(op, FrameKind.Construct));
        Assert.False(EjectRules.StartsGlide(op, FrameKind.Planet));
        Assert.False(EjectRules.StartsGlide(op with { LocalVelocity = new Vector3(0.4f, 0, 0) }, FrameKind.Construct));
        Assert.False(EjectRules.StartsGlide(op with { NewParent = FrameRef.Of(new EntityId(3)) }, FrameKind.Construct));
        Assert.Equal(6, (int)MovementMode.Gliding);
    }
}

using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Ships;
using Horyzont.Testing;

using static Horyzont.Core.Tests.BlockEntities.FunctionalBlockFixture;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35, D14 and D15: the one function that chooses where a player appears. The bed gives up to four places beside its cell, in the axes of the construct, in a fixed order; what
/// is safe is data from the probe of the terrain; the planet "up" decides about a tilted construct, never the Y axis of the construct.
/// </summary>
public sealed class RespawnPointsTests
{
    private static readonly Vector3 _origin = new(0f, 4000f, 0f);
    private static readonly RespawnPose _start = new(WorldFactory.HomePlanet, new Vector3(100f, 4000.1f, 0f), Quaternion.Identity);

    /// <summary>A world whose only construct is the bed block of player 1 at <see cref="FunctionalBlocks.BedCell"/>.</summary>
    private static FunctionalBlockFixture BedWorld(Quaternion rotation, bool setRespawn = true)
    {
        var f = new FunctionalBlockFixture(WorldFactory.CreateWithFunctionalBlocks(2, withConstruct: false));
        f.Apply(new ConstructCreateOp(
            ConstructId, FrameRef.Of(WorldFactory.HomePlanet), _origin, rotation, FunctionalBlocks.BedCell, BlockState.Create(FunctionalBlocks.BedType, 0)));
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player1));
        if (setRespawn)
        {
            f.Apply(new RespawnPointSetOp(Player1, BedAt(FunctionalBlocks.BedCell)));
        }

        return f;
    }

    private static RespawnContext Context(FunctionalBlockFixture f, params bool[] safe) => new(f.State, Player1, safe, _start);

    /// <summary>
    /// Feet beside the bed on one side: the centre of the bed cell moved one cell along the axis (composed with the pose of the construct), then half a cell down along the planet up
    /// (K6-35 review A, P3-6; the planet up here is +Y, the construct stands at the top of the planet).
    /// </summary>
    private static Vector3 Expected(Quaternion rotation, Vector3 side)
    {
        var cell = FunctionalBlocks.BedCell;
        var local = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f) + side;
        return _origin + Vector3.Transform(local, rotation) - new Vector3(0f, 0.5f, 0f);
    }

    private static readonly Vector3[] _sides = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ, -Vector3.UnitZ];

    [Fact]
    public void Resolve_OwnBed_SelectsSafeCandidate()
    {
        var f = BedWorld(Quaternion.Identity);
        var candidates = new List<RespawnPose>();

        Assert.True(RespawnPoints.TryGetBedCandidates(f.State, Player1, candidates));

        // The order is fixed: +X, -X, +Z, -Z of the bed cell in the axes of the construct.
        Assert.Equal(4, candidates.Count);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(WorldFactory.HomePlanet, candidates[i].Planet);
            Assert.True(Vector3.Distance(Expected(Quaternion.Identity, _sides[i]), candidates[i].Position) < 1e-3f, $"candidate {i}: {candidates[i].Position}");
        }

        var chosen = RespawnPoints.Resolve(Context(f, false, true, true, true));
        Assert.Equal(RespawnSource.Bed, chosen.Source);
        Assert.Equal(candidates[1], chosen.Pose);

        // The first safe one, not the nearest: unsafe candidates in front are skipped, a missing flag counts as unsafe.
        Assert.Equal(candidates[3], RespawnPoints.Resolve(Context(f, false, false, false, true)).Pose);
        Assert.Equal(candidates[0], RespawnPoints.Resolve(Context(f, true, true, true, true)).Pose);
        Assert.Equal(RespawnSource.WorldStart, RespawnPoints.Resolve(Context(f, false, false)).Source);
    }

    [Fact]
    public void Resolve_OtherPlayersBedOrNoOwnerState_UsesHomeStart()
    {
        var f = BedWorld(Quaternion.Identity);
        f.Apply(SetBedState(FunctionalBlocks.BedCell, Player2));

        var chosen = RespawnPoints.Resolve(Context(f, true, true, true, true));

        Assert.Equal(RespawnSource.WorldStart, chosen.Source);
        Assert.Equal(_start, chosen.Pose);
        Assert.False(RespawnPoints.TryGetBedCandidates(f.State, Player1, []));
    }

    [Fact]
    public void Resolve_MissingBlockedOrFlyingBed_UsesHomeStart()
    {
        // No respawn point at all.
        var none = BedWorld(Quaternion.Identity, setRespawn: false);
        Assert.Equal(_start, RespawnPoints.Resolve(Context(none, true, true, true, true)).Pose);

        // Every place beside the bed is blocked.
        var blocked = BedWorld(Quaternion.Identity);
        Assert.True(RespawnPoints.TryGetBedCandidates(blocked.State, Player1, []));
        var blockedPoint = RespawnPoints.Resolve(Context(blocked, false, false, false, false));
        Assert.Equal(RespawnSource.WorldStart, blockedPoint.Source);
        Assert.Equal(_start, blockedPoint.Pose);

        // A ship in flight (not parked), and a ship without a flight state, are not a place to appear.
        var ship = BedWorld(Quaternion.Identity);
        ship.Apply(new ShipModeSetOp(ConstructId, ConstructMode.Ship, FrameRef.Of(WorldFactory.HomePlanet), 0, _origin, Quaternion.Identity));
        Assert.Equal(RespawnSource.WorldStart, RespawnPoints.Resolve(Context(ship, true, true, true, true)).Source);
        Assert.True(ship.Sim.Sim.UpdateShipState(ConstructId, new ShipStateRecord(ConstructId, 1f, Vector3.Zero, Vector3.Zero, ShipStateFlags.None)));
        Assert.Equal(RespawnSource.WorldStart, RespawnPoints.Resolve(Context(ship, true, true, true, true)).Source);

        // The same ship parked is.
        Assert.True(ship.Sim.Sim.UpdateShipState(ConstructId, new ShipStateRecord(ConstructId, 1f, Vector3.Zero, Vector3.Zero, ShipStateFlags.Parked)));
        Assert.Equal(RespawnSource.Bed, RespawnPoints.Resolve(Context(ship, true, true, true, true)).Source);
    }

    [Fact]
    public void Resolve_WorldStartOnAnotherPlanetThanHome_Throws()
    {
        var f = BedWorld(Quaternion.Identity, setRespawn: false);
        var wrong = new RespawnContext(f.State, Player1, null, _start with { Planet = new PlanetId(f.State.HomePlanet.Value + 1) });

        Assert.Throws<ArgumentException>(() => RespawnPoints.Resolve(wrong));
        Assert.Throws<ArgumentNullException>(() => RespawnPoints.Resolve(null!));
    }

    [Theory]
    [InlineData(30f, 4)]
    [InlineData(90f, 3)]
    public void Resolve_TiltedConstruct_UsesPlanetUp(float degrees, int expectedCandidates)
    {
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180f);
        var f = BedWorld(rotation);
        var candidates = new List<RespawnPose>();

        Assert.True(RespawnPoints.TryGetBedCandidates(f.State, Player1, candidates));

        // A side that points down along the planet up (here +Y at the origin) is left out; the others keep the fixed order. At 90 degrees -X points straight down.
        var expected = _sides.Where(s => Vector3.Dot(Vector3.Transform(s, rotation), Vector3.UnitY) > -0.7072f).Select(s => Expected(rotation, s)).ToList();
        Assert.Equal(expectedCandidates, expected.Count);
        Assert.Equal(expected.Count, candidates.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(Vector3.Distance(candidates[i].Position, expected[i]) < 1e-3f, $"candidate {i}: {candidates[i].Position} instead of {expected[i]}");
        }

        var chosen = RespawnPoints.Resolve(Context(f, true, true, true, true));
        Assert.Equal(RespawnSource.Bed, chosen.Source);
        Assert.Equal(candidates[0], chosen.Pose);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(90f)]
    public void Candidates_BodyRotation_UpIsThePlanetUpAndFacesTheBed(float degrees)
    {
        // K6-35 review A, P2-3: the body is not left as the identity; its up is the radial direction at the candidate and its forward points at the bed.
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, degrees * MathF.PI / 180f);
        var f = BedWorld(rotation);
        var candidates = new List<RespawnPose>();
        Assert.True(RespawnPoints.TryGetBedCandidates(f.State, Player1, candidates));
        Assert.NotEmpty(candidates);

        var cell = FunctionalBlocks.BedCell;
        var bedCentre = _origin + Vector3.Transform(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f), rotation);
        foreach (var candidate in candidates)
        {
            var up = Vector3.Normalize(candidate.Position);
            Assert.True(Vector3.Distance(Vector3.Transform(Vector3.UnitY, candidate.Rotation), up) < 1e-3f, $"up of {candidate.Position}");
            Assert.True(Math.Abs(candidate.Rotation.Length() - 1f) < 1e-4f);

            var toBed = bedCentre - candidate.Position;
            toBed -= up * Vector3.Dot(toBed, up);
            if (toBed.Length() < 0.1f)
            {
                continue; // straight above the bed: no horizontal direction to face
            }

            var forward = Vector3.Transform(-Vector3.UnitZ, candidate.Rotation);
            Assert.True(Vector3.Dot(Vector3.Normalize(toBed), forward) > 0.999f, $"forward of {candidate.Position}");
        }
    }

    [Fact]
    public void Resolve_UpsideDownConstruct_UsesHomeStart()
    {
        var f = BedWorld(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI));
        var candidates = new List<RespawnPose>();

        Assert.False(RespawnPoints.TryGetBedCandidates(f.State, Player1, candidates));

        Assert.Empty(candidates);
        var chosen = RespawnPoints.Resolve(Context(f, true, true, true, true));
        Assert.Equal(RespawnSource.WorldStart, chosen.Source);
        Assert.Equal(_start, chosen.Pose);
    }
}

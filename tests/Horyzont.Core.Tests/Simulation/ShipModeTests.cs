using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

namespace Horyzont.Core.Tests.Simulation;

public sealed class ShipModeTests
{
    private static readonly FrameRef _planet = FrameRef.Of(WorldFactory.HomePlanet);
    private static readonly EntityId _ship = ShipFixtures.ConstructId;

    private static Vector3 Origin(SimFixture f) => new(0, f.Radius, 0);

    /// <summary>An empty world in which the 30-block ship is built by operations (so checkpoints and the journal know it).</summary>
    private static SimFixture BuiltShip()
    {
        var f = new SimFixture(state: ShipFixtures.CreateWorld(null));
        foreach (var op in ShipFixtures.BuildOperations(_ship, ShipFixtures.Thirty, Origin(f)))
        {
            f.Sim.ApplyServerOperation(op);
        }

        return f;
    }

    private static ShipModeSetOp SetMode(SimFixture f, ConstructMode mode, Vector3? origin = null, Quaternion? rotation = null)
    {
        var construct = f.State.Constructs.All.Single(c => c.Id == _ship);
        return new ShipModeSetOp(_ship, mode, construct.Parent, construct.ParentSeq, origin ?? construct.Origin, rotation ?? construct.Rotation);
    }

    [Fact]
    public void ShipModeSetOp_Apply_SetsModeAndPose_VersionPlusOne()
    {
        var f = BuiltShip();
        var construct = f.State.Constructs.All.Single();
        var versionBefore = construct.Version;
        var pose = new Vector3(5f, f.Radius + 1f, -3f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);

        var applied = f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship, pose, rotation));

        Assert.Equal(ConstructMode.Ship, construct.Mode);
        Assert.Equal(pose, construct.Origin);
        Assert.Equal(rotation, construct.Rotation);
        Assert.Equal(versionBefore + 1, construct.Version);
        var change = Assert.Single(applied.ConstructChanges);
        Assert.Equal(new ConstructChange(_ship, versionBefore + 1, false), change);
        Assert.Equal(30, construct.BlockCount);
        Assert.Equal(_planet, construct.Parent);
        Assert.Equal(0u, construct.ParentSeq);
        Assert.Equal(f.State.LastOpId, applied.Id);
    }

    [Fact]
    public void ShipModeSetOp_Apply_BumpsVersionByOne()
    {
        var f = BuiltShip();
        var construct = f.State.Constructs.All.Single();
        var seen = new List<uint>();
        f.Sim.Applied += a => seen.AddRange(a.ConstructChanges.Select(c => c.Version));

        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship));
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Static));
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship));

        Assert.Equal(new[] { construct.Version - 2, construct.Version - 1, construct.Version }, seen);
    }

    [Fact]
    public void ShipModeSetOp_Preconditions_Rejected()
    {
        var f = BuiltShip();
        var valid = SetMode(f, ConstructMode.Ship);
        var hashBefore = StateHasher.Hash(f.State);
        var journalBefore = f.Journal.Entries.Count;

        ShipModeSetOp[] broken =
        [
            valid with { Construct = new EntityId(999) },
            valid with { Mode = ConstructMode.Static },
            valid with { Mode = (ConstructMode)7 },
            valid with { Parent = FrameRef.Of(new PlanetId(2)) },
            valid with { ParentSeq = 1 },
            valid with { Origin = new Vector3(float.NaN, 0, 0) },
            valid with { Origin = new Vector3(0, float.PositiveInfinity, 0) },
            valid with { Rotation = new Quaternion(0, 0, 0, 0) },
            valid with { Rotation = new Quaternion(0, 0, 0, 2) },
        ];
        foreach (var op in broken)
        {
            Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(op));
        }

        Assert.Equal(hashBefore, StateHasher.Hash(f.State));
        Assert.Equal(journalBefore, f.Journal.Entries.Count);
        Assert.Equal(ConstructMode.Static, f.State.Constructs.All.Single().Mode);
    }

    [Fact]
    public void ShipModeSetOp_Preconditions_StaticOnlyInPlanetFrame()
    {
        var f = BuiltShip();
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            new EntityId(2), _planet, new Vector3(100, f.Radius, 0), Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(f.Stone, 0)));
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship));
        f.Sim.ApplyServerOperation(new EntityReparentOp(
            _ship, FrameRef.Of(new EntityId(2)), 1, Vector3.Zero, Quaternion.Identity, Vector3.Zero, null));

        var toStatic = SetMode(f, ConstructMode.Static);

        Assert.Equal(FrameRef.Of(new EntityId(2)), toStatic.Parent);
        Assert.Throws<InvalidOperationException>(() => f.Sim.ApplyServerOperation(toStatic));
        Assert.Equal(ConstructMode.Ship, f.State.Constructs.All.Single(c => c.Id == _ship).Mode);
    }

    [Fact]
    public void ShipModeSetOp_JournalReplay_SameStateHash()
    {
        var live = BuiltShip();
        live.Ticks(3);
        live.Sim.ApplyServerOperation(SetMode(live, ConstructMode.Ship, new Vector3(9f, live.Radius + 2f, 4f), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f)));
        var hashAsShip = StateHasher.Hash(live.State);
        live.Ticks(2);
        live.Sim.ApplyServerOperation(SetMode(live, ConstructMode.Static, new Vector3(-8f, live.Radius, 6f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1.1f)));
        var hashStatic = StateHasher.Hash(live.State);
        var fresh = new SimFixture(state: ShipFixtures.CreateWorld(null));

        foreach (var entry in live.Journal.Entries)
        {
            fresh.Sim.Replay(entry);
        }

        Assert.NotEqual(hashAsShip, hashStatic);
        Assert.Equal(hashStatic, StateHasher.Hash(fresh.State));
        var replayed = fresh.State.Constructs.All.Single();
        Assert.Equal(ConstructMode.Static, replayed.Mode);
        Assert.Equal(new Vector3(-8f, live.Radius, 6f), replayed.Origin);
        Assert.Equal(live.State.Constructs.All.Single().Version, replayed.Version);
    }

    [Fact]
    public void ShipModeSetOp_ShipPoseIsNotHashed_ModeAndVersionAre()
    {
        var a = BuiltShip();
        var b = BuiltShip();

        a.Sim.ApplyServerOperation(SetMode(a, ConstructMode.Ship, new Vector3(1, a.Radius, 0)));
        b.Sim.ApplyServerOperation(SetMode(b, ConstructMode.Ship, new Vector3(50, a.Radius + 20, -7)));

        // The pose of a ship is not part of the hash (it is flight state, K5-24); the mode and the version are.
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        Assert.NotEqual(StateHasher.Hash(BuiltShip().State), StateHasher.Hash(a.State));
    }

    [Fact]
    public void ShipModeSetOp_CheckpointRoundTrip_ModePreserved()
    {
        var f = BuiltShip();
        var checkpoints = new List<WorldCheckpoint> { f.State.CaptureCheckpoint() };
        f.State.CompleteCheckpoint(checkpoints[^1]);
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship, new Vector3(3f, f.Radius + 1f, 2f)));
        checkpoints.Add(f.State.CaptureCheckpoint());
        f.State.CompleteCheckpoint(checkpoints[^1]);
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Static, new Vector3(-4f, f.Radius, 1f)));
        f.Sim.ApplyServerOperation(SetMode(f, ConstructMode.Ship, new Vector3(7f, f.Radius + 3f, 1f)));
        checkpoints.Add(f.State.CaptureCheckpoint());
        f.State.CompleteCheckpoint(checkpoints[^1]);

        var header = Assert.Single(checkpoints[^1].UpsertedConstructs);
        Assert.Equal(ConstructMode.Ship, header.Mode);
        var loaded = Checkpoints.CheckpointTests.Load(f.State, checkpoints);

        var construct = loaded.Constructs.All.Single();
        Assert.Equal(ConstructMode.Ship, construct.Mode);
        Assert.Equal(f.State.Constructs.All.Single().Version, construct.Version);
        Assert.Equal(StateHasher.Hash(f.State), StateHasher.Hash(loaded));
    }
}

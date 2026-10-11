using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World;
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

    // ----- intents ---------------------------------------------------------------------------------------------------------------------------

    private static readonly CellPos _cockpitCell = new(2, 2, 0);

    /// <summary>A small ship: engine, cockpit and hull side by side on the ground (y = 0 layer).</summary>
    private static readonly ShipBlock[] _small =
    [
        new(ShipFixtures.Engine, new CellPos(0, 0, 0), ShipFixtures.EngineFacing(Face.NegY)),
        new(ShipFixtures.Cockpit, new CellPos(1, 0, 0)),
        new(ShipFixtures.Hull, new CellPos(2, 0, 0)),
    ];

    private static (SimFixture F, PlayerId P) Rig(
        IReadOnlyList<ShipBlock> blocks, Vector3 feetOffset, ConstructMode mode = ConstructMode.Static, Action<FakeTerrainService>? terrain = null)
    {
        var state = ShipFixtures.CreateWorld(blocks, new Vector3(0, 4000, 0), mode);
        var f = new SimFixture(state: state, content: ShipFixtures.ContentFor(state));
        terrain?.Invoke(f.Terrain);
        var player = f.Join();
        f.MovePlayer(player, new Vector3(0, f.Radius, 0) + feetOffset);
        return (f, player);
    }

    /// <summary>Intent aimed from the player's eye at the centre of a cell of the ship.</summary>
    private static ShipModeIntent Aim(SimFixture f, PlayerId player, CellPos cell, ConstructMode target)
    {
        var center = new Vector3(0, f.Radius, 0) + new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        var eye = f.Eye(player);
        return new ShipModeIntent(_ship, cell, target, eye, SimFixture.Direction(eye, center));
    }

    private static readonly Vector3 _inFrontOfNose = new(2.5f, 0f, -2f);

    private static void AssertRejectedWithoutChange(SimFixture f, PlayerId player, ShipModeIntent intent, RejectReason expected)
    {
        var hash = StateHasher.Hash(f.State);
        var journal = f.Journal.Entries.Count;

        var result = f.Sim.Submit(player, intent);

        Assert.Equal(expected, result.Reason);
        Assert.Equal(hash, StateHasher.Hash(f.State));
        Assert.Equal(journal, f.Journal.Entries.Count);
    }

    [Fact]
    public void ShipModeIntent_Cockpit_AcceptedBothWays_ResolvesToOperationWithCurrentFrame()
    {
        var (f, player) = Rig(ShipFixtures.Thirty, _inFrontOfNose);
        var construct = f.State.Constructs.All.Single();
        var version = construct.Version;

        var toShip = f.Sim.Submit(player, Aim(f, player, _cockpitCell, ConstructMode.Ship));

        Assert.True(toShip.IsAccepted, toShip.Reason.ToString());
        var op = Assert.IsType<ShipModeSetOp>(toShip.Applied!.Operation);
        Assert.Equal(new ShipModeSetOp(_ship, ConstructMode.Ship, _planet, 0, construct.Origin, construct.Rotation), op);
        Assert.Equal(ConstructMode.Ship, construct.Mode);
        Assert.Equal(version + 1, construct.Version);

        var toStatic = f.Sim.Submit(player, Aim(f, player, _cockpitCell, ConstructMode.Static));

        Assert.True(toStatic.IsAccepted, toStatic.Reason.ToString());
        Assert.Equal(ConstructMode.Static, construct.Mode);
        Assert.Equal(version + 2, construct.Version);
        Assert.Equal(2, f.Journal.Entries.Count(e => e.Operation is ShipModeSetOp));
    }

    [Fact]
    public void ShipModeIntent_NoCockpitAtCell_RejectedNoCockpit()
    {
        var (f, player) = Rig(ShipFixtures.Thirty, _inFrontOfNose);

        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(2, 1, 1), ConstructMode.Ship), RejectReason.NoCockpit);
        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(2, 1, 0), ConstructMode.Ship), RejectReason.NoCockpit);
    }

    [Fact]
    public void ShipModeIntent_EmptyContentTable_NothingIsACockpit()
    {
        var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, new Vector3(0, 4000, 0));
        var f = new SimFixture(state: state);
        var player = f.Join();
        f.MovePlayer(player, new Vector3(0, f.Radius, 0) + _inFrontOfNose);

        AssertRejectedWithoutChange(f, player, Aim(f, player, _cockpitCell, ConstructMode.Ship), RejectReason.NoCockpit);
    }

    [Fact]
    public void ShipModeIntent_NoEngine_RejectedNoEngine()
    {
        var noEngine = _small.Where(b => b.Key != ShipFixtures.Engine).ToArray();
        var (f, player) = Rig(noEngine, new Vector3(1.5f, 0f, -2f));

        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship), RejectReason.NoEngine);
    }

    [Fact]
    public void ShipModeIntent_BlockInTerrain_RejectedShipAnchored()
    {
        // One hull block below the ground: its centre is 0.5 m inside the terrain sphere.
        ShipBlock[] buried = [.. _small, new ShipBlock(ShipFixtures.Hull, new CellPos(1, -1, 0))];
        var (f, player) = Rig(buried, new Vector3(1.5f, 0f, -2f));

        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship), RejectReason.ShipAnchored);
        Assert.True(f.Terrain.SolidSamples > 0);
    }

    [Fact]
    public void ShipModeIntent_CornerBelowGround_RejectedShipAnchored()
    {
        // The hull block stands on a bump: the ground reaches 0.15 m above its floor, so its bottom corners (0.1 m inside) are in it.
        var (f, player) = Rig(_small, new Vector3(1.5f, 0f, -2f), terrain: t =>
            t.SolidModel = (_, p) => p.Y < 4000f + 0.15f && p.X > 1.9f && p.X < 3f);

        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship), RejectReason.ShipAnchored);
    }

    [Fact]
    public void ShipModeIntent_TouchingGround_Accepted()
    {
        // The ground reaches the cell floor and no further than 0.05 m inside: touching is not anchoring.
        var (f, player) = Rig(_small, new Vector3(1.5f, 0f, -2f), terrain: t =>
            t.SolidModel = (_, p) => p.Y < 4000f + 0.05f);

        var result = f.Sim.Submit(player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship));

        Assert.True(result.IsAccepted, result.Reason.ToString());
        Assert.Equal(ConstructMode.Ship, f.State.Constructs.All.Single().Mode);
    }

    [Fact]
    public void ShipModeIntent_AreaNotLoaded_Rejected()
    {
        var (f, player) = Rig(_small, new Vector3(1.5f, 0f, -2f), terrain: t => t.AllAreasLoaded = false);

        AssertRejectedWithoutChange(f, player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship), RejectReason.AreaNotLoaded);
    }

    [Fact]
    public void ShipModeIntent_OutOfReachOrNoLineOfSight_Rejected()
    {
        var (far, farPlayer) = Rig(ShipFixtures.Thirty, new Vector3(2.5f, 0f, -20f));
        AssertRejectedWithoutChange(far, farPlayer, Aim(far, farPlayer, _cockpitCell, ConstructMode.Ship), RejectReason.OutOfReach);

        var (behind, behindPlayer) = Rig(ShipFixtures.Thirty, new Vector3(2.5f, 0f, 7f));
        AssertRejectedWithoutChange(behind, behindPlayer, Aim(behind, behindPlayer, _cockpitCell, ConstructMode.Ship), RejectReason.NoLineOfSight);
    }

    [Fact]
    public void ShipModeIntent_OtherRejections_InOrder()
    {
        var (f, player) = Rig(ShipFixtures.Thirty, _inFrontOfNose);
        var valid = Aim(f, player, _cockpitCell, ConstructMode.Ship);

        AssertRejectedWithoutChange(f, player, valid with { Construct = new EntityId(77) }, RejectReason.UnknownConstruct);
        AssertRejectedWithoutChange(f, player, valid with { CockpitCell = new CellPos(5000, 0, 0) }, RejectReason.CellOutOfBounds);
        AssertRejectedWithoutChange(f, player, valid with { CockpitCell = new CellPos(40, 40, 40) }, RejectReason.CellEmpty);
        AssertRejectedWithoutChange(f, player, valid with { TargetMode = ConstructMode.Static }, RejectReason.InvalidParameters);
        AssertRejectedWithoutChange(f, player, valid with { TargetMode = (ConstructMode)5 }, RejectReason.InvalidParameters);
        AssertRejectedWithoutChange(f, player, valid with { RayDirection = new Vector3(2, 0, 0) }, RejectReason.InvalidParameters);
        AssertRejectedWithoutChange(f, player, valid with { RayOrigin = valid.RayOrigin + new Vector3(0, 0, -30) }, RejectReason.RayOriginMismatch);
    }

    [Fact]
    public void ShipModeIntent_PlayerOnAnotherPlanet_RejectedWrongPlanet()
    {
        var other = new PlanetParams(new PlanetId(2), new SystemId(1), 9, "core:test/1");
        var state = ShipFixtures.CreateWorld(ShipFixtures.Thirty, new Vector3(0, 4000, 0));
        var f = new SimFixture(state: state, content: ShipFixtures.ContentFor(state));
        var player = f.Join();
        f.State.Planets.Add(other);
        f.Sim.ApplyServerOperation(new EntityReparentOp(
            f.PlayerOf(player).Entity, FrameRef.Of(other.Id), 1, new Vector3(0, 4000, 0), Quaternion.Identity, Vector3.Zero, null));

        var result = f.Sim.Submit(player, Aim(f, player, _cockpitCell, ConstructMode.Ship));

        Assert.Equal(RejectReason.WrongPlanet, result.Reason);
    }

    [Fact]
    public void ShipConstruct_RemoveLastCockpitOrEngine_Rejected()
    {
        var (f, player) = Rig(_small, new Vector3(1.5f, 0f, -2f));
        Assert.True(f.Sim.Submit(player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Ship)).IsAccepted);
        var hash = StateHasher.Hash(f.State);
        static RemoveBlockIntent Remove(CellPos cell) => new(_ship, cell);

        Assert.Equal(RejectReason.NoEngine, f.Sim.Submit(player, Remove(new CellPos(0, 0, 0))).Reason);
        Assert.Equal(RejectReason.NoCockpit, f.Sim.Submit(player, Remove(new CellPos(1, 0, 0))).Reason);
        Assert.Equal(hash, StateHasher.Hash(f.State));

        // Hull is not special; and a static construct loses its cockpit freely.
        Assert.True(f.Sim.Submit(player, Remove(new CellPos(2, 0, 0))).IsAccepted);
        Assert.True(f.Sim.Submit(player, Aim(f, player, new CellPos(1, 0, 0), ConstructMode.Static)).IsAccepted);
        Assert.True(f.Sim.Submit(player, Remove(new CellPos(0, 0, 0))).IsAccepted);
    }

    [Fact]
    public void ShipConstruct_RemoveOneOfSeveralEngines_Accepted()
    {
        var (f, player) = Rig(ShipFixtures.Thirty, _inFrontOfNose, ConstructMode.Ship);

        var result = f.Sim.Submit(player, new RemoveBlockIntent(_ship, new CellPos(2, 1, 0)));

        Assert.True(result.IsAccepted, result.Reason.ToString());
    }

    [Fact]
    public void ShipConstruct_PlaceBlockIntoTerrain_RejectedShipAnchored()
    {
        // Stand in front of the small ship and aim at the lower face of its cockpit: the new cell lies under the ground.
        var feet = new Vector3(1.5f, 0f, -2f);
        var (ship, shipPlayer) = Rig(_small, feet, ConstructMode.Ship);
        var (parked, parkedPlayer) = Rig(_small, feet, ConstructMode.Static);
        var place = new PlaceBlockIntent(new ConstructFaceTarget(_ship, new CellPos(2, 0, 0), Face.NegY), ship.Stone, 0);

        var anchored = ship.Sim.Submit(shipPlayer, place);
        var free = parked.Sim.Submit(parkedPlayer, place);

        Assert.Equal(RejectReason.ShipAnchored, anchored.Reason);
        Assert.True(free.IsAccepted, free.Reason.ToString());
    }

    [Fact]
    public void ShipModeIntent_UsesTheBlockBucket_RejectionsAreFree()
    {
        var (f, player) = Rig(ShipFixtures.Thirty, _inFrontOfNose);
        var valid = Aim(f, player, new CellPos(2, 1, 1), ConstructMode.Ship);

        for (var i = 0; i < f.Rules.BlockEditBurst; i++)
        {
            Assert.Equal(RejectReason.NoCockpit, f.Sim.Submit(player, valid).Reason);
        }

        // Rejected intents do not consume tokens, so the bucket is still full for a legal switch.
        var legal = f.Sim.Submit(player, Aim(f, player, _cockpitCell, ConstructMode.Ship));
        Assert.True(legal.IsAccepted, legal.Reason.ToString());

        for (var i = 0; i < f.Rules.BlockEditBurst; i++)
        {
            f.Sim.Submit(player, Aim(f, player, _cockpitCell, i % 2 == 0 ? ConstructMode.Static : ConstructMode.Ship));
        }

        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(player, Aim(f, player, _cockpitCell, ConstructMode.Static)).Reason);
    }
}

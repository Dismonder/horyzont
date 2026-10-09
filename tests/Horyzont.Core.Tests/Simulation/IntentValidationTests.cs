using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

public sealed class IntentValidationTests
{
    private static readonly Dictionary<RejectReason, Func<RejectReason>> _scenarios = new()
    {
        [RejectReason.UnknownPlayer] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(new PlayerId(99), f.Dig(p, f.Ground(2))).Reason;
        },
        [RejectReason.PlayerOffline] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var dig = f.Dig(p, f.Ground(2));
            f.Sim.LeaveAllOnlinePlayers();
            return f.Sim.Submit(p, dig).Reason;
        },
        [RejectReason.RateLimited] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            for (var i = 0; i < 5; i++)
            {
                Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
            }

            return f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason;
        },
        [RejectReason.InvalidParameters] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayDirection = new Vector3(float.NaN, 0, 0) }).Reason;
        },
        [RejectReason.RayOriginMismatch] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayOrigin = f.Eye(p) + new Vector3(5, 0, 0) }).Reason;
        },
        [RejectReason.NoHit] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayDirection = Vector3.UnitY }).Reason;
        },
        [RejectReason.OutOfReach] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var c = f.BuildOnTerrain(p, 2);
            f.MovePlayer(p, f.Ground(20) + new Vector3(0, 0.1f, 0));
            return f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(0, 0, 0))).Reason;
        },
        [RejectReason.AreaNotLoaded] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            f.Terrain.AllAreasLoaded = false;
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason;
        },
        [RejectReason.BelowCore] = () =>
        {
            var f = new SimFixture(terrain: new FakeTerrainService(3500f));
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason;
        },
        [RejectReason.OutsideAtmosphere] = () =>
        {
            var f = new SimFixture(terrain: new FakeTerrainService(5200f));
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Eye(p) + new Vector3(1, -1, 0))).Reason;
        },
        [RejectReason.UnknownPlanet] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { Planet = new PlanetId(99) }).Reason;
        },
        [RejectReason.UnknownConstruct] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, new RemoveBlockIntent(new EntityId(999), new CellPos(0, 0, 0))).Reason;
        },
        [RejectReason.CellOutOfBounds] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var c = f.BuildOnTerrain(p, 2);
            return f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(5000, 0, 0))).Reason;
        },
        [RejectReason.CellOccupied] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var c = f.BuildOnTerrain(p, 2);
            f.SetBlock(c, new CellPos(0, 1, 0));
            return f.Sim.Submit(p, new PlaceBlockIntent(new ConstructFaceTarget(c, new CellPos(0, 0, 0), Face.PosY), f.Stone, 0)).Reason;
        },
        [RejectReason.CellEmpty] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var c = f.BuildOnTerrain(p, 2);
            return f.Sim.Submit(p, new RemoveBlockIntent(c, new CellPos(0, 3, 0))).Reason;
        },
        [RejectReason.UnknownBlockType] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(2)) with { BlockTypeId = 999 }).Reason;
        },
        [RejectReason.UnknownMaterial] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Fill(p, f.Ground(4), material: 99)).Reason;
        },
        [RejectReason.IntersectsPlayer] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            return f.Sim.Submit(p, f.Fill(p, f.Ground(1), radius: 3f)).Reason;
        },
        [RejectReason.NoLineOfSight] = () =>
        {
            var f = new SimFixture();
            var p = f.Join();
            var far = f.BuildOnTerrain(p, 4);
            f.BuildOnTerrain(p, 2);
            return f.Sim.Submit(p, new RemoveBlockIntent(far, new CellPos(0, 0, 0))).Reason;
        },
        [RejectReason.WrongPlanet] = () =>
        {
            var second = WorldFactory.HomePlanetParams with { Id = new PlanetId(2) };
            var f = new SimFixture(state: WorldFactory.CreateWith(WorldFactory.HomePlanetParams, second));
            var p = f.Join();
            return f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { Planet = new PlanetId(2) }).Reason;
        },
    };

    public static TheoryData<RejectReason> AllReasons()
    {
        var data = new TheoryData<RejectReason>();
        foreach (var reason in Enum.GetValues<RejectReason>())
        {
            // ServerReadOnly is answered by the server hooks in safe mode (K2-10), never by the simulation.
            if (reason is not (RejectReason.None or RejectReason.ServerReadOnly))
            {
                data.Add(reason);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllReasons))]
    public void Submit_Scenario_ReturnsEachRejectReason(RejectReason reason)
    {
        Assert.True(_scenarios.ContainsKey(reason), $"No scenario reaches {reason}.");

        Assert.Equal(reason, _scenarios[reason]());
    }

    [Fact]
    public void Submit_OtherInvalidParameters_Rejected()
    {
        var f = new SimFixture();
        var p = f.Join();
        var dig = f.Dig(p, f.Ground(2));

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, dig with { RayDirection = new Vector3(2, 0, 0) }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, dig with { Radius = 0.1f }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, dig with { MaterialId = 1 }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, dig with { Mode = (TerrainEditMode)9 }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(2)) with { Orientation = 24 }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, null!).Reason);
        Assert.DoesNotContain(f.Journal.Entries, e => e.Actor == p);
    }

    [Fact]
    public void Submit_UnknownPlayerAndBadParameters_UnknownPlayerFirst()
    {
        var f = new SimFixture();
        var p = f.Join();
        var bad = f.Dig(p, f.Ground(2)) with { RayDirection = new Vector3(float.NaN, 0, 0) };

        Assert.Equal(RejectReason.UnknownPlayer, f.Sim.Submit(new PlayerId(7), bad).Reason);
    }

    [Fact]
    public void Submit_OfflineAndBadParameters_OfflineFirst()
    {
        var f = new SimFixture();
        var p = f.Join();
        var bad = f.Dig(p, f.Ground(2)) with { RayDirection = new Vector3(float.NaN, 0, 0) };
        f.Sim.LeaveAllOnlinePlayers();

        Assert.Equal(RejectReason.PlayerOffline, f.Sim.Submit(p, bad).Reason);
    }

    [Fact]
    public void Submit_BadParametersAndFarOrigin_InvalidParametersFirst()
    {
        var f = new SimFixture();
        var p = f.Join();
        var bad = f.Dig(p, f.Ground(2)) with { RayDirection = new Vector3(float.NaN, 0, 0), RayOrigin = new Vector3(100, 100, 100) };

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(p, bad).Reason);
    }

    [Fact]
    public void Submit_RateLimitedAndFarOrigin_RayOriginMismatchFirst()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }

        var far = f.Dig(p, f.Ground(2)) with { RayOrigin = f.Eye(p) + new Vector3(0, 5, 0) };

        Assert.Equal(RejectReason.RayOriginMismatch, f.Sim.Submit(p, far).Reason);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { Planet = new PlanetId(99) }).Reason);
    }

    [Fact]
    public void Submit_RateClasses_AreIndependent()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }

        Assert.True(f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(3))).IsAccepted);
    }

    [Fact]
    public void Submit_Rejected_DoesNotConsumeToken()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(RejectReason.NoHit, f.Sim.Submit(p, f.Dig(p, f.Ground(2)) with { RayDirection = Vector3.UnitY }).Reason);
        }

        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }
    }

    [Fact]
    public void Submit_PlaceOnTerrainBehindConstruct_NoLineOfSight()
    {
        var f = new SimFixture();
        var p = f.Join();
        f.BuildOnTerrain(p, 2);

        Assert.Equal(RejectReason.NoLineOfSight, f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(4))).Reason);
        Assert.Equal(RejectReason.NoLineOfSight, f.Sim.Submit(p, f.Dig(p, f.Ground(4))).Reason);
    }

    [Fact]
    public void Submit_PlaceOnFaceIntoPlayer_IntersectsPlayer()
    {
        var f = new SimFixture();
        var p = f.Join();
        var c = f.BuildOnTerrain(p, 2);
        f.MovePlayer(p, f.Ground(1.0f) + new Vector3(0, 0.1f, 0));

        var result = f.Sim.Submit(p, new PlaceBlockIntent(new ConstructFaceTarget(c, new CellPos(0, 0, 0), Face.NegX), f.Stone, 0));

        Assert.Equal(RejectReason.IntersectsPlayer, result.Reason);
    }

    private static EntityId AddConstruct(SimFixture f, Vector3 origin)
    {
        var id = f.State.EntityIds.PeekNext();
        f.Sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(SimFixture.Planet), origin, Quaternion.Identity, new CellPos(0, 0, 0), BlockState.Create(f.Stone, 0)));
        return id;
    }

    private static PlaceBlockIntent OnTopOf(SimFixture f, EntityId construct) =>
        new(new ConstructFaceTarget(construct, new CellPos(0, 0, 0), Face.PosY), f.Stone, 0);

    [Fact]
    public void Submit_PlaceOnFaceTouchingOtherConstructByFace_Accepted()
    {
        var f = new SimFixture();
        var p = f.Join();
        var a = AddConstruct(f, new Vector3(2f, 4002f, 0f));
        AddConstruct(f, new Vector3(3f, 4003f, 0f));

        Assert.Equal(RejectReason.None, f.Sim.Submit(p, OnTopOf(f, a)).Reason);
    }

    [Fact]
    public void Submit_PlaceOnFaceOverlappingOtherConstruct_CellOccupied()
    {
        var f = new SimFixture();
        var p = f.Join();
        var a = AddConstruct(f, new Vector3(2f, 4002f, 0f));
        AddConstruct(f, new Vector3(2.75f, 4003f, 0f));

        Assert.Equal(RejectReason.CellOccupied, f.Sim.Submit(p, OnTopOf(f, a)).Reason);
        Assert.DoesNotContain(f.Journal.Entries, e => e.Actor == p);
    }

    [Theory]
    [InlineData(3.15f, RejectReason.CellOccupied)]
    [InlineData(3.25f, RejectReason.None)]
    public void Submit_PlaceOnTerrainTurned45DegreesNextToConstruct_DecidedByExactOverlap(float x, RejectReason expected)
    {
        var f = new SimFixture();
        var p = f.Join();
        f.BuildOnTerrain(p, 2);
        f.MovePlayer(p, f.Ground(5) + new Vector3(0, 0.1f, 0));
        var place = f.PlaceOnTerrain(p, f.Ground(x));
        var diagonal = new Vector3(-MathF.Sqrt(0.5f), 0f, -MathF.Sqrt(0.5f));
        place = place with { Target = ((TerrainTarget)place.Target) with { LookForward = diagonal } };

        Assert.Equal(expected, f.Sim.Submit(p, place).Reason);
    }

    [Fact]
    public void Submit_PlaceChecks_AreaNotLoadedThenCellOccupiedThenIntersectsPlayer()
    {
        var f = new SimFixture();
        var p = f.Join();
        var a = AddConstruct(f, new Vector3(2f, 4002f, 0f));
        var b = AddConstruct(f, new Vector3(2.75f, 4003f, 0f));
        f.MovePlayer(p, new Vector3(2.5f, 4002.5f, 0.5f));
        f.Terrain.AllAreasLoaded = false;

        Assert.Equal(RejectReason.AreaNotLoaded, f.Sim.Submit(p, OnTopOf(f, a)).Reason);
        f.Terrain.AllAreasLoaded = true;
        Assert.Equal(RejectReason.CellOccupied, f.Sim.Submit(p, OnTopOf(f, a)).Reason);
        f.Sim.Submit(p, new RemoveBlockIntent(b, new CellPos(0, 0, 0)));
        Assert.Equal(RejectReason.IntersectsPlayer, f.Sim.Submit(p, OnTopOf(f, a)).Reason);
    }

    [Fact]
    public void Submit_SixthTerrainEditInWindow_RateLimitedEvenWithTokenAndRejectionCostsNothing()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }

        // 12 ticks refill one token of the bucket, but the sliding window of one second still holds five edits.
        f.Ticks(12);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        f.Ticks(47);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
        f.Ticks(1);

        // Tick 60: the first five edits (tick 0) left the window (0, 60]; the rejections above did not use it.
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted, $"edit {i} at tick 60");
        }

        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(p, f.Dig(p, f.Ground(2))).Reason);
    }

    [Fact]
    public void Submit_BlockEditsAreNotCountedInTerrainWindow()
    {
        var f = new SimFixture();
        var p = f.Join();
        for (var i = 0; i < 5; i++)
        {
            Assert.True(f.Sim.Submit(p, f.Dig(p, f.Ground(2))).IsAccepted);
        }

        Assert.True(f.Sim.Submit(p, f.PlaceOnTerrain(p, f.Ground(-3))).IsAccepted);
    }

    private static TerrainToolIntent DigShiftedToward(SimFixture f, PlayerId p, Vector3 target, float shift)
    {
        var eye = f.Eye(p);
        var direction = Vector3.Normalize(target - eye);
        return new TerrainToolIntent(SimFixture.Planet, eye + (direction * shift), direction, 0.5f, TerrainEditMode.Remove, 0);
    }

    private static PlaceBlockIntent PlaceShiftedToward(SimFixture f, PlayerId p, Vector3 target, float shift)
    {
        var eye = f.Eye(p);
        var direction = Vector3.Normalize(target - eye);
        return new PlaceBlockIntent(new TerrainTarget(SimFixture.Planet, eye + (direction * shift), direction, -Vector3.UnitZ), f.Stone, 0);
    }

    private static Vector3 GroundAtEyeDistance(SimFixture f, PlayerId p, float distance)
    {
        var eye = f.Eye(p);
        var low = 0f;
        var high = 12f;
        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2f;
            if (Vector3.Distance(eye, f.Ground(mid)) < distance)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return f.Ground(low);
    }

    [Fact]
    public void Submit_TerrainHitBeyondEyeReachWithRayOriginInTolerance_OutOfReach()
    {
        var f = new SimFixture();
        var p = f.Join();

        var far = DigShiftedToward(f, p, GroundAtEyeDistance(f, p, 9.4f), 1.9f);
        var near = DigShiftedToward(f, p, GroundAtEyeDistance(f, p, 7.9f), 1.9f);

        Assert.Equal(RejectReason.OutOfReach, f.Sim.Submit(p, far).Reason);
        Assert.True(f.Sim.Submit(p, near).IsAccepted);
    }

    [Fact]
    public void Submit_PlaceOnTerrainBeyondEyeReachWithRayOriginInTolerance_OutOfReach()
    {
        var f = new SimFixture();
        var p = f.Join();

        var far = PlaceShiftedToward(f, p, GroundAtEyeDistance(f, p, 9.4f), 1.9f);
        var near = PlaceShiftedToward(f, p, GroundAtEyeDistance(f, p, 7.9f), 1.9f);

        Assert.Equal(RejectReason.OutOfReach, f.Sim.Submit(p, far).Reason);
        Assert.True(f.Sim.Submit(p, near).IsAccepted);
    }
}

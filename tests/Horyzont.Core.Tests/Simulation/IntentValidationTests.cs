using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Props;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Simulation;

public sealed class IntentValidationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submit_OffsetRayGrazesSlopeNearTarget_Accepted(bool placement)
    {
        var f = CurvedSlopeFixture();
        var p = f.Join();
        var intent = OffsetSlopeIntent(f, p, 0.56f, placement, out var target);
        var eye = f.Eye(p);
        var distance = Vector3.Distance(eye, target);
        Assert.True(f.Terrain.Raycast(SimFixture.Planet, eye, Vector3.Normalize(target - eye), distance, out var first));
        // The eye ray grazes the same curved surface about 22 cm before the intended hit.
        Assert.InRange(distance - first.Distance, 0.20f, 0.24f);

        Assert.Equal(RejectReason.None, f.Sim.Submit(p, intent).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submit_OffsetRayBehindTerrainRidgeAtLeastOneMeter_NoLineOfSight(bool placement)
    {
        var f = CurvedSlopeFixture();
        var p = f.Join();
        var intent = OffsetSlopeIntent(f, p, 0.65f, placement, out var target);
        var eye = f.Eye(p);
        var distance = Vector3.Distance(eye, target);
        Assert.True(f.Terrain.Raycast(SimFixture.Planet, eye, Vector3.Normalize(target - eye), distance, out var first));
        // The segment to the far surface crosses more than one metre of solid terrain.
        Assert.InRange(distance - first.Distance, 1f, 2f);

        Assert.Equal(RejectReason.NoLineOfSight, f.Sim.Submit(p, intent).Reason);
    }

    private static SimFixture CurvedSlopeFixture()
    {
        // A small sphere makes both sides of a curved ridge reachable in the eight metre tool range.
        var planet = WorldFactory.HomePlanetParams with { RadiusMeters = 10f, CoreDepthMeters = 4f };
        return new SimFixture(terrain: new FakeTerrainService(10f), state: WorldFactory.CreateWith(planet));
    }

    private static Intent OffsetSlopeIntent(SimFixture f, PlayerId p, float angle, bool placement, out Vector3 target)
    {
        target = new Vector3(10f * MathF.Sin(angle), 10f * MathF.Cos(angle), 0f);
        var origin = f.Eye(p) + (Vector3.UnitX * 1.5f);
        var direction = Vector3.Normalize(target - origin);
        Assert.InRange(Vector3.Distance(origin, f.Eye(p)), 0f, f.Rules.MaxRayOriginErrorMeters);
        Assert.True(f.Terrain.Raycast(SimFixture.Planet, origin, direction, f.Rules.ReachMeters, out var hit));
        Assert.InRange(Vector3.Distance(hit.Position, target), 0f, 0.001f);
        return placement
            ? new PlaceBlockIntent(new TerrainTarget(SimFixture.Planet, origin, direction, -Vector3.UnitZ), f.Stone, 0)
            : new TerrainToolIntent(SimFixture.Planet, origin, direction, 0.5f, TerrainEditMode.Remove, 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Submit_RawRayOriginBehindOneMeterWall_NoLineOfSight(bool placement)
    {
        var f = new SimFixture();
        var p = f.Join();
        var eye = f.Eye(p);
        // One metre along X, spanning the ray's height; the raw origin lies beyond it.
        AddConstruct(f, new Vector3(0.25f, eye.Y - 0.5f, -0.5f));
        var target = f.Ground(4);
        Intent intent = placement ? PlaceShiftedToward(f, p, target, 1.5f) : DigShiftedToward(f, p, target, 1.5f);

        Assert.Equal(RejectReason.NoLineOfSight, f.Sim.Submit(p, intent).Reason);
    }

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
        [RejectReason.InventoryFull] = () =>
        {
            var f = ItemScenario();
            f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(1), ItemFixture.Gem, 5, limit: 5));
            f.Apply(ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Gem, 5, limit: 5));
            return f.Sim.Sim.Submit(ItemFixture.Player1, MoveGem(1, 5, 5)).Reason;
        },
        [RejectReason.NotEnoughItems] = () =>
        {
            var f = ItemScenario();
            f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(1), ItemFixture.Gem, 3, limit: 5));
            return f.Sim.Sim.Submit(ItemFixture.Player1, MoveGem(1, 4, 3)).Reason;
        },
        [RejectReason.ContainerChanged] = () =>
        {
            var f = ItemScenario();
            f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(1), ItemFixture.Gem, 3, limit: 5));
            return f.Sim.Sim.Submit(ItemFixture.Player1, MoveGem(1, 1, 2)).Reason;
        },
        [RejectReason.ContainerLocked] = () =>
        {
            var f = ItemScenario(players: 2);
            f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(1), ItemFixture.Gem, 3, limit: 5));
            var foreign = ItemMoveTarget.ToSlot(new SlotRef(WorldFactory.BagOf(2), 0));
            return f.Sim.Sim.Submit(ItemFixture.Player1, MoveGem(1, 3, 3) with { To = foreign }).Reason;
        },
    };

    private static ItemFixture ItemScenario(int players = 1) => new(players, content: SurvivalContentFactory.CreateTest());

    private static ItemMoveIntent MoveGem(int hotbarSlot, int count, int expected) => new(
        ItemFixture.HotbarSlot(hotbarSlot), ItemMoveTarget.ToSlot(ItemFixture.BagSlot(0)), (ushort)count, ItemFixture.Gem, (ushort)expected);

    public static TheoryData<RejectReason> AllReasons()
    {
        var data = new TheoryData<RejectReason>();
        foreach (var reason in Enum.GetValues<RejectReason>())
        {
            // ServerReadOnly is answered by the server hooks in safe mode (K2-10), never by the simulation.
            // The Space reasons (30-39) are reached by the ship scenarios of ShipModeTests (K5-23).
            if (reason is not (RejectReason.None or RejectReason.ServerReadOnly) && (byte)reason < 30)
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

    /// <summary>
    /// The reasons of K6-30 (54, 57): <see cref="AllReasons"/> stops below 30, so each new code gets its scenario here. The member names of the
    /// enum arrive with the protocol (step B2).
    /// </summary>
    [Theory]
    [InlineData((byte)54)]
    [InlineData((byte)57)]
    public void Submit_FunctionalBlockScenario_ReturnsNewReason(byte number)
    {
        var f = FunctionalBlockFixture.ForIntents(2);
        var actual = number switch
        {
            54 => NotOwnerScenario(f),
            57 => ContainerNotEmptyScenario(f),
            _ => throw new ArgumentOutOfRangeException(nameof(number)),
        };

        Assert.Equal((RejectReason)number, actual);
    }

    /// <summary>The reason of K6-31 (53): the bare hand on a tree that needs a tool; the same hit with the tool is accepted first.</summary>
    [Fact]
    public void Submit_PropScenario_ReturnsToolTierTooLow()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        f.StandBeside(tree, 3f);
        Assert.True(f.Hit(tree.Id, 0).IsAccepted);

        Assert.Equal(RejectReason.ToolTierTooLow, f.Hit(tree.Id, 9).Reason);
    }

    private static RejectReason NotOwnerScenario(FunctionalBlockFixture f)
    {
        f.SetUpBed(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player2);
        return f.SubmitAs(FunctionalBlockFixture.Player1, new BlockInteractIntent(FunctionalBlockFixture.ConstructId, FunctionalBlocks.BedCell, BlockInteractAction.Sleep)).Reason;
    }

    private static RejectReason ContainerNotEmptyScenario(FunctionalBlockFixture f)
    {
        var chest = f.SetUpChestAt(FunctionalBlocks.ChestCell, FunctionalBlockFixture.Player1);
        f.Give(chest, 0, 1);
        return f.SubmitAs(FunctionalBlockFixture.Player1, new RemoveBlockIntent(FunctionalBlockFixture.ConstructId, FunctionalBlocks.ChestCell)).Reason;
    }

    /// <summary>
    /// The reasons of K6-32 (44, 45, 46): each new code gets its scenario here. The member names of the enum arrive with the protocol (step B2).
    /// </summary>
    [Theory]
    [InlineData((byte)44)]
    [InlineData((byte)45)]
    [InlineData((byte)46)]
    public void Submit_CraftingScenario_ReturnsNewReason(byte number)
    {
        var f = FunctionalBlockFixture.ForCrafting();
        f.EnsureBlock(CraftingWorld.BenchCell, CraftingBlocks.WorkbenchType);
        f.Stock(FunctionalBlockFixture.Bag, 0, CraftingWorld.Brick, 4);
        f.Discover(FunctionalBlockFixture.Player1, CraftingWorld.Brick);

        // The valid twin of every scenario: the bench recipe at the bench, in reach, with a discovered ingredient.
        f.Accept(FunctionalBlockFixture.Player1, CraftingWorld.Craft("t:bench1", 1, CraftingWorld.BenchCell));
        var actual = number switch
        {
            44 => f.SubmitAs(FunctionalBlockFixture.Player1, CraftingWorld.Craft("t:bench1")).Reason,
            45 => f.SubmitAs(FunctionalBlockFixture.Player1, CraftingWorld.Craft("t:hand")).Reason,
            46 => TooFar(f),
            _ => throw new ArgumentOutOfRangeException(nameof(number)),
        };

        Assert.Equal((RejectReason)number, actual);
    }

    private static RejectReason TooFar(FunctionalBlockFixture f)
    {
        ReachPlacement.MoveEyeToDistance(f, FunctionalBlockFixture.Player1, CraftingWorld.BenchCell, 8.5f);
        return f.SubmitAs(FunctionalBlockFixture.Player1, CraftingWorld.Craft("t:bench1", 1, CraftingWorld.BenchCell)).Reason;
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

    [Theory]
    [InlineData(10, true)]
    [InlineData(70, false)]
    public void Submit_PlaceOnFaceOfDeeplyNestedConstruct_RejectedWhenOverlapTestCannotFollowChain(int depth, bool accepted)
    {
        var f = new SimFixture();
        var p = f.Join();
        f.Terrain.AllAreasLoaded = true;
        var tip = AddConstruct(f, f.Ground(2));
        for (var i = 0; i < depth; i++)
        {
            // Straight into the store: the operations only create constructs directly in a planet frame.
            var nested = f.State.Constructs.Create(new EntityId(5000 + (uint)i), FrameRef.Of(tip), Vector3.Zero, Quaternion.Identity);
            nested.SetBlock(new CellPos(0, 0, 0), BlockState.Create(f.Stone, 0));
            tip = nested.Id;
        }

        var result = f.Sim.Submit(p, OnTopOf(f, tip));

        Assert.Equal(accepted, result.IsAccepted);
        Assert.Equal(accepted ? RejectReason.None : RejectReason.WrongPlanet, result.Reason);
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

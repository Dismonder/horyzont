using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Crafting;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Hashing;

/// <summary>D7 of K6-29W: the survival section is appended only when the world has item types, containers or a moved container counter.</summary>
public sealed class SurvivalHashTests
{
    /// <summary>Hash of <see cref="WorldFactory.CreateDefault"/> recorded at commit eee8f17 (before stage A of K6-29W).</summary>
    private const ulong DefaultWorldHashBeforeItems = 17523897430694564880UL;

    /// <summary>Hash of <see cref="WorldWithItems"/> recorded at commit b5e427e (start of K6-30, before the block entities and the respawn points).</summary>
    private const ulong WorldWithItemsHashBeforeBlockEntities = 15528921043589756334UL;

    /// <summary>
    /// Hash of <see cref="WorldWithBlockEntities"/> as K6-30 leaves it (commit 2fa618b), before discoveries, progress keys and stations. Re-pinned when K6-30
    /// was merged: K6-30 rebuilt the chest fixture as one CompoundOp (container and state together, so the chest-consistency check holds), which changes the
    /// hash of this mock-up (container ids, versions) without any production change to the hash. The old value, 256735958396369194, was recorded at 06814a8;
    /// the new one was measured on a clean K6-30 checkout with the same world, and K6-32 agrees with it.
    /// </summary>
    private const ulong WorldWithBlockEntitiesHashBeforeCrafting = 8435541483743415689UL;

    /// <summary>Hash of <see cref="WorldWithCrafting"/> recorded at commit ffd89bb (start of K6-33, before the food slots and the vitals).</summary>
    private const ulong WorldWithCraftingHashBeforeFood = 2493131761482920173UL;

    [Fact]
    public void WorldWithCraftingWithoutFood_HashUnchanged() =>
        Assert.Equal(WorldWithCraftingHashBeforeFood, StateHasher.Hash(WorldWithCrafting()));

    [Fact]
    public void FoodOnly_OpensSurvivalSection()
    {
        // A record with only food is a record of progress (D4): the world with it is another world than the one without, and two equal ones agree.
        var a = new FoodWorld();
        var b = new FoodWorld();
        var without = new FoodWorld();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(without.State));

        a.Apply(FoodWorld.Set(FoodWorld.Player1, FoodWorld.Slot(FoodWorld.Berries, 100)));
        b.Apply(FoodWorld.Set(FoodWorld.Player1, FoodWorld.Slot(FoodWorld.Berries, 100)));

        Assert.Equal(1, a.State.Progress.Count);
        Assert.NotEqual(StateHasher.Hash(without.State), StateHasher.Hash(a.State));
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        // Clearing the food removes the record and the food section. The hash covers the last operation id, so the twin is a world that applied two
        // operations too: a food set that sets nothing, twice (the first of them is the same operation id as the berries above).
        a.Apply(FoodWorld.Set(FoodWorld.Player1));
        Assert.Equal(0, a.State.Progress.Count);
        without.Apply(FoodWorld.Set(FoodWorld.Player1));
        without.Apply(FoodWorld.Set(FoodWorld.Player1));
        Assert.Equal(StateHasher.Hash(without.State), StateHasher.Hash(a.State));
    }

    [Fact]
    public void Food_ChangesHash()
    {
        var variants = new Func<FoodSlot[]>[]
        {
            () => [],
            () => [FoodWorld.Slot(FoodWorld.Berries, 100)],
            () => [FoodWorld.Slot(FoodWorld.Berries, 101)],
            () => [FoodWorld.Slot(FoodWorld.Berries, 0)],
            () => [FoodWorld.Slot(FoodWorld.Jam, 100)],
            () => [FoodWorld.Slot(FoodWorld.Berries, 100), FoodWorld.Slot(FoodWorld.Jam, 100)],
            () => [FoodWorld.Slot(FoodWorld.Berries, 100), FoodWorld.Slot(FoodWorld.Jam, 100), FoodWorld.Slot(FoodWorld.Bread, 100)],
        };

        var hashes = variants.Select(v =>
        {
            var f = new FoodWorld();
            f.Apply(FoodWorld.Set(FoodWorld.Player1, v()));
            return StateHasher.Hash(f.State);
        }).ToList();

        Assert.Equal(variants.Length, hashes.Distinct().Count());

        // The food of another player is another world.
        var other = new FoodWorld(players: 2);
        other.Apply(FoodWorld.Set(FoodWorld.Player2, FoodWorld.Slot(FoodWorld.Berries, 100)));
        var first = new FoodWorld(players: 2);
        first.Apply(FoodWorld.Set(FoodWorld.Player1, FoodWorld.Slot(FoodWorld.Berries, 100)));
        Assert.NotEqual(StateHasher.Hash(first.State), StateHasher.Hash(other.State));
    }

    [Fact]
    public void FoodSlotOrder_ChangesHash()
    {
        var berries = FoodWorld.Slot(FoodWorld.Berries, 100);
        var jam = FoodWorld.Slot(FoodWorld.Jam, 200);
        var orders = new[]
        {
            new[] { berries, jam },
            new[] { jam, berries },
            new[] { berries, default, jam },
            new[] { default, berries, jam },
            new[] { default, default, berries },
            new[] { default, berries },
            new[] { berries },
        };

        var hashes = orders.Select(o =>
        {
            var f = new FoodWorld();
            f.Apply(FoodWorld.Set(FoodWorld.Player1, o));
            return StateHasher.Hash(f.State);
        }).ToList();

        Assert.Equal(orders.Length, hashes.Distinct().Count());
    }

    [Fact]
    public void FoodWithBedAndDiscoveries_AllThreeCount()
    {
        var bedOnly = new FoodWorld();
        bedOnly.Blocks.SetUpBed(FunctionalBlocks.BedCell, FoodWorld.Player1);
        var withFood = new FoodWorld();
        withFood.Blocks.SetUpBed(FunctionalBlocks.BedCell, FoodWorld.Player1);
        withFood.Apply(FoodWorld.Set(FoodWorld.Player1, FoodWorld.Slot(FoodWorld.Berries, 100)));
        var withDiscovery = new FoodWorld();
        withDiscovery.Blocks.SetUpBed(FunctionalBlocks.BedCell, FoodWorld.Player1);
        withDiscovery.Apply(new ItemDiscoverOp(FoodWorld.Player1, FoodWorld.Stone));

        var hashes = new[] { bedOnly, withFood, withDiscovery }.Select(x => StateHasher.Hash(x.State)).ToList();

        Assert.Equal(3, hashes.Distinct().Count());
    }

    [Fact]
    public void Vitals_OnlineAndOffline_DoNotChangeHash()
    {
        var a = new FoodWorld();
        var b = new FoodWorld();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        // Online: the vitals are outside the hash, like the pose.
        Assert.True(a.Simulation.UpdatePlayerVitals(FoodWorld.Player1, new PlayerVitals(50f, 20f, new Tick(5000), true)));
        Assert.Equal(StateHasher.Hash(b.State), StateHasher.Hash(a.State));

        // Offline: the player left with the vitals (the checkpoint keeps them), and the worlds still agree.
        foreach (var f in new[] { a, b })
        {
            var state = f.Blocks.Sim.PlayerOf(FoodWorld.Player1);
            f.Simulation.ApplyServerOperation(new PlayerLeaveOp(FoodWorld.Player1, state.Frame, state.ParentSeq, state.Position, System.Numerics.Quaternion.Identity, 0f));
        }

        Assert.NotNull(a.Blocks.Sim.PlayerOf(FoodWorld.Player1).Vitals);
        Assert.Null(b.Blocks.Sim.PlayerOf(FoodWorld.Player1).Vitals);
        Assert.Equal(StateHasher.Hash(b.State), StateHasher.Hash(a.State));
    }

    [Fact]
    public void WorldWithoutItems_HashUnchanged() => Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(WorldFactory.CreateDefault()));

    [Fact]
    public void WorldWithItemsWithoutBlockEntities_HashUnchanged() =>
        Assert.Equal(WorldWithItemsHashBeforeBlockEntities, StateHasher.Hash(WorldWithItems()));

    [Fact]
    public void WorldWithBlockEntitiesWithoutCrafting_HashUnchanged() =>
        Assert.Equal(WorldWithBlockEntitiesHashBeforeCrafting, StateHasher.Hash(WorldWithBlockEntities()));

    [Fact]
    public void BlockEntityChange_ChangesHash()
    {
        var a = new FunctionalBlockFixture(players: 2);
        var b = new FunctionalBlockFixture(players: 2);
        var c = new FunctionalBlockFixture(players: 2);
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, PlayerId.None));
        b.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player2));
        c.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player2));

        var hashes = new[] { StateHasher.Hash(a.State), StateHasher.Hash(b.State) };
        Assert.NotEqual(hashes[0], hashes[1]);
        Assert.Equal(hashes[1], StateHasher.Hash(c.State));
        var withoutState = new FunctionalBlockFixture(players: 2);
        Assert.NotEqual(StateHasher.Hash(withoutState.State), hashes[0]);

        var chestA = new FunctionalBlockFixture();
        var chestB = new FunctionalBlockFixture();
        chestA.SetUpChest(FunctionalBlocks.ChestCell);
        chestB.SetUpChest(FunctionalBlocks.ChestCell, access: ContainerAccess.Private);
        Assert.NotEqual(StateHasher.Hash(chestB.State), StateHasher.Hash(chestA.State));
        Assert.NotEqual(StateHasher.Hash(new FunctionalBlockFixture().State), StateHasher.Hash(chestA.State));
    }

    [Fact]
    public void RespawnPointChange_ChangesHash()
    {
        var a = new FunctionalBlockFixture();
        var b = new FunctionalBlockFixture();
        a.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1));
        b.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1));
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(new RespawnPointSetOp(FunctionalBlockFixture.Player1, FunctionalBlockFixture.BedAt(FunctionalBlocks.BedCell)));
        b.Apply(new RespawnPointSetOp(FunctionalBlockFixture.Player1, new BedRef(FunctionalBlockFixture.ConstructId, FunctionalBlocks.BedCell, FunctionalBlocks.BedType)));
        var withPoint = StateHasher.Hash(a.State);
        Assert.Equal(withPoint, StateHasher.Hash(b.State));

        // The same bed state without the respawn point is another world.
        var other = new FunctionalBlockFixture();
        other.Apply(FunctionalBlockFixture.SetBedState(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1));
        Assert.NotEqual(withPoint, StateHasher.Hash(other.State));
        Assert.True(a.State.Progress.TryGet(FunctionalBlockFixture.Player1, out var progress));
        Assert.NotNull(progress.RespawnBed);
    }

    [Fact]
    public void FunctionStateChange_ChangesHash()
    {
        var a = new FunctionalBlockFixture();
        var b = new FunctionalBlockFixture();
        var c = new FunctionalBlockFixture();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(FunctionalBlockFixture.SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));
        b.Apply(FunctionalBlockFixture.SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 2));
        c.Apply(FunctionalBlockFixture.SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));

        Assert.NotEqual(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(c.State));
        Assert.NotEqual(StateHasher.Hash(a.State), StateHasher.Hash(new FunctionalBlockFixture().State));
    }

    [Fact]
    public void ItemTypesOnly_ChangeHash()
    {
        var withItems = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"], SurvivalContentFactory.ItemKeys);
        var withoutItems = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"], []);

        Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(withoutItems));
        Assert.NotEqual(StateHasher.Hash(withoutItems), StateHasher.Hash(withItems));
    }

    [Fact]
    public void SlotChange_ChangesHash()
    {
        var a = new ItemFixture();
        var b = new ItemFixture();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(ItemFixture.Create(ItemFixture.BagSlot(1), ItemFixture.Stone, 3));
        b.Apply(ItemFixture.Create(ItemFixture.BagSlot(2), ItemFixture.Stone, 3));
        var aHash = StateHasher.Hash(a.State);

        Assert.NotEqual(aHash, StateHasher.Hash(b.State));
        Assert.Equal(a.Container(ItemFixture.Bag).Version, b.Container(ItemFixture.Bag).Version);
        b.Apply(ItemFixture.Create(ItemFixture.BagSlot(2), ItemFixture.Stone, 1));
        Assert.NotEqual(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
    }

    [Fact]
    public void NextContainerId_ChangesHash()
    {
        var first = Builder(1).Build();
        var moved = Builder(2).Build();

        Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(first));
        Assert.Equal(2UL, moved.ContainerIds.NextValue);
        Assert.NotEqual(StateHasher.Hash(first), StateHasher.Hash(moved));
    }

    [Fact]
    public void ContainerProperties_EachChangeTheHash()
    {
        // Variants differ from the baseline (chest of player 1, owner 1, shared, 4 slots, version 1) in exactly one property.
        var variants = new Func<WorldState>[]
        {
            () => Chest(),
            () => Chest(version: 3),
            () => Chest(access: ContainerAccess.Private),
            () => Chest(owner: new PlayerId(2)),
            () => Chest(kind: ContainerKind.Equipment),
            () => Chest(size: 5),
            () => Chest(host: new PlayerId(2)),
        };

        var hashes = variants.Select(v => StateHasher.Hash(v())).ToList();

        Assert.Equal(variants.Length, hashes.Distinct().Count());
    }

    [Fact]
    public void Discovery_ChangesHash()
    {
        var a = new CraftingFixture(players: 2);
        var b = new CraftingFixture(players: 2);
        var c = new CraftingFixture(players: 2);
        var d = new CraftingFixture(players: 2);
        var e = new CraftingFixture(players: 2);
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(CraftingFixture.Discover(CraftingFixture.Player1, CraftingFixture.Stone));
        b.Apply(CraftingFixture.Discover(CraftingFixture.Player1, CraftingFixture.Stone));
        c.Apply(CraftingFixture.Discover(CraftingFixture.Player1, CraftingFixture.Soil));
        d.Apply(CraftingFixture.Discover(CraftingFixture.Player2, CraftingFixture.Stone));
        e.Apply(CraftingFixture.Discover(CraftingFixture.Player1, CraftingFixture.Stone));
        e.Apply(CraftingFixture.Discover(CraftingFixture.Player1, CraftingFixture.Soil));

        var hashes = new[] { a, c, d, e, new CraftingFixture(players: 2) }.Select(x => StateHasher.Hash(x.State)).ToList();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    [Fact]
    public void ProgressKey_ChangesHash()
    {
        var a = new CraftingFixture();
        var b = new CraftingFixture();
        var c = new CraftingFixture();
        var d = new CraftingFixture();
        var e = new CraftingFixture();
        a.Apply(CraftingFixture.Grant("test:a"));
        b.Apply(CraftingFixture.Grant("test:a"));
        c.Apply(CraftingFixture.Grant("test:b"));
        d.Apply(CraftingFixture.Grant("test:a"));
        d.Apply(CraftingFixture.Grant("test:b"));
        e.Apply(CraftingFixture.Grant("test:b"));
        e.Apply(CraftingFixture.Grant("test:a"));

        // The keys are a set: the order of granting does not matter.
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        Assert.Equal(StateHasher.Hash(d.State), StateHasher.Hash(e.State));
        var hashes = new[] { a, c, d, new CraftingFixture() }.Select(x => StateHasher.Hash(x.State)).ToList();
        Assert.Equal(hashes.Count, hashes.Distinct().Count());
    }

    [Fact]
    public void ProgressKeyOnly_OpensSurvivalSection()
    {
        // A world with no item types, containers, states or progress: before the keys the survival section was not hashed at all.
        var without = new SimFixture();
        var a = new SimFixture();
        var b = new SimFixture();
        Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(without.State));

        a.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:a"));
        b.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:a"));

        Assert.NotEqual(StateHasher.Hash(without.State), StateHasher.Hash(a.State));
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));
        var other = new SimFixture();
        other.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:b"));
        Assert.NotEqual(StateHasher.Hash(a.State), StateHasher.Hash(other.State));
    }

    [Fact]
    public void StationStateStarted_ChangesHash()
    {
        var states = new (string Recipe, Tick? Started)[]
        {
            (string.Empty, null),
            ("t:a", null),
            ("t:a", new Tick(100)),
            ("t:a", new Tick(101)),
            ("t:b", new Tick(100)),

            // Started = Tick(0) is another state than Started = null: a flag and a tick, not the tick alone (review A1, 4-7).
            ("t:a", new Tick(0)),
        };
        var hashes = states.Select(s =>
        {
            var f = new CraftingFixture();
            f.SetUpStation(CraftingFixture.StationCell, s.Recipe, s.Started);
            return StateHasher.Hash(f.State);
        }).ToList();

        Assert.Equal(states.Length, hashes.Distinct().Count());
        var again = new CraftingFixture();
        again.SetUpStation(CraftingFixture.StationCell, "t:a", new Tick(100));
        Assert.Equal(hashes[2], StateHasher.Hash(again.State));
    }

    /// <summary>
    /// The K6-29W world of the survival tests: two players with their containers, the start kit of <see cref="SurvivalContentFactory"/>, stone and soil
    /// moved around the bags and a shared chest container hosted by a player. No block with state, no respawn point.
    /// </summary>
    private static WorldState WorldWithItems()
    {
        var f = new ItemFixture(players: 2);
        f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(0), ItemFixture.Tool, 1, limit: 1, source: ItemSource.Starter));
        f.Apply(ItemFixture.Create(ItemFixture.HotbarSlot(1), ItemFixture.Gem, 3, limit: 5, source: ItemSource.Starter));
        f.Apply(ItemFixture.Create(ItemFixture.BagSlot(0), ItemFixture.Stone, 40, source: ItemSource.Harvest));
        f.Apply(ItemFixture.Create(ItemFixture.BagSlot(1), ItemFixture.Soil, 12, source: ItemSource.Harvest));
        f.Apply(ItemFixture.Transfer(ItemFixture.BagSlot(0), ItemFixture.BagSlot(4), ItemFixture.Stone, 15));
        var chest = f.State.ContainerIds.PeekNext();
        f.Apply(new CompoundOp(
        [
            new ContainerCreateOp(chest, ContainerKind.Chest, ContainerHost.Player(ItemFixture.Player1), ItemFixture.Player1, ContainerAccess.Shared, 8),
            ItemFixture.Transfer(ItemFixture.BagSlot(4), new SlotRef(chest, 2), ItemFixture.Stone, 10),
        ]));
        return f.State;
    }

    /// <summary>
    /// The K6-30 world of the functional block tests: a chest block with a container holding stone and a bed block with the respawn point of
    /// player 1. No discoveries, no progress keys, no station.
    /// </summary>
    private static WorldState WorldWithBlockEntities()
    {
        var f = new FunctionalBlockFixture(players: 2);
        var chest = f.SetUpChest(FunctionalBlocks.ChestCell);
        f.Give(chest, 1, 7);
        f.SetUpBed(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1);
        return f.State;
    }

    /// <summary>
    /// The K6-32 world of the crafting tests: the K6-30 bed with the respawn point of player 1, a station in work, a discovery of player 2 and a
    /// progress key. No food, no vitals.
    /// </summary>
    private static WorldState WorldWithCrafting()
    {
        var f = new CraftingFixture(players: 2);
        f.Blocks.SetUpBed(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1);
        f.SetUpStation(CraftingFixture.StationCell, "t:a", new Tick(100));
        f.Apply(CraftingFixture.Discover(CraftingFixture.Player2, CraftingFixture.Gem));
        f.Apply(CraftingFixture.Grant("test:boss"));
        return f.State;
    }

    private static WorldState Chest(
        uint version = 1, ContainerAccess access = ContainerAccess.Shared, PlayerId? owner = null, ContainerKind kind = ContainerKind.Chest,
        ushort size = 4, PlayerId? host = null)
    {
        var world = WorldState.CreateNew(
            WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], [WorldFactory.StoneBlockKey], ["stone", "dirt"], SurvivalContentFactory.ItemKeys);
        var sim = new SimFixture(state: world);
        var first = sim.Join(1);
        var second = sim.Join(2);
        var hostPlayer = host ?? first;
        var id = world.ContainerIds.PeekNext();
        sim.Sim.ApplyServerOperation(new ContainerCreateOp(id, kind, ContainerHost.Player(hostPlayer), owner ?? first, access, size));
        for (var v = 1U; v < version; v += 2)
        {
            sim.Sim.ApplyServerOperation(ItemFixture.Create(new SlotRef(id, 0), ItemFixture.Stone, 1, source: ItemSource.Admin));
            sim.Sim.ApplyServerOperation(ItemFixture.Destroy(new SlotRef(id, 0), ItemFixture.Stone, 1));
        }

        Assert.Equal(version, world.Containers.TryGet(id, out var created) ? created.Version : 0U);
        Assert.NotEqual(first, second);
        return world;
    }

    private static WorldStateBuilder Builder(ulong nextContainerId)
    {
        var template = WorldFactory.CreateDefault();
        var builder = new WorldStateBuilder(template.Id, template.GalaxySeed);
        builder.SetProgress(OpId.None, default, 1, 1);
        builder.SetNextContainerId(nextContainerId);
        builder.AddPlanet(WorldFactory.HomePlanetParams);
        foreach (var entry in template.BlockTypes.Entries)
        {
            builder.AddBlockType(entry);
        }

        foreach (var entry in template.Materials.Entries)
        {
            builder.AddMaterial(entry);
        }

        return builder;
    }
}

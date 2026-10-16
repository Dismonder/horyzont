using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.Tests.Props;
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

    /// <summary>Hash of <see cref="WorldWithBlockEntities"/> recorded at the start of K6-31 (commit a1781ee, before the harvested props).</summary>
    private const ulong WorldWithBlockEntitiesHashBeforeProps = 17678442467951482955UL;

    [Fact]
    public void WorldWithoutItems_HashUnchanged() => Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(WorldFactory.CreateDefault()));

    [Fact]
    public void WorldWithItemsWithoutBlockEntities_HashUnchanged() =>
        Assert.Equal(WorldWithItemsHashBeforeBlockEntities, StateHasher.Hash(WorldWithItems()));

    [Fact]
    public void WorldWithBlockEntitiesWithoutProps_HashUnchanged() =>
        Assert.Equal(WorldWithBlockEntitiesHashBeforeProps, StateHasher.Hash(WorldWithBlockEntities()));

    [Fact]
    public void HarvestProp_ChangesHash()
    {
        var a = new PropFixture();
        var b = new PropFixture();
        var c = new PropFixture();
        var d = new PropFixture();
        var e = new PropFixture();
        Assert.Equal(StateHasher.Hash(a.State), StateHasher.Hash(b.State));

        a.Apply(PropFixture.Harvest(PropFixture.Tree(0), 100));
        b.Apply(PropFixture.Harvest(PropFixture.Tree(0), 100));
        c.Apply(PropFixture.Harvest(PropFixture.Tree(1), 100));
        d.Apply(PropFixture.Harvest(PropFixture.Tree(0), 101));
        e.Apply(PropFixture.Harvest(PropFixture.Rock(0), 100));

        // The same prop and tick hash the same; another prop, tick or slot is another world, and so is a world without the harvest.
        var hash = StateHasher.Hash(a.State);
        Assert.Equal(hash, StateHasher.Hash(b.State));
        Assert.NotEqual(hash, StateHasher.Hash(c.State));
        Assert.NotEqual(hash, StateHasher.Hash(d.State));
        Assert.NotEqual(hash, StateHasher.Hash(e.State));
        Assert.NotEqual(hash, StateHasher.Hash(new PropFixture().State));
        Assert.NotEqual(StateHasher.Hash(c.State), StateHasher.Hash(e.State));
    }

    [Fact]
    public void HarvestedPropsOnly_InWorldWithoutItems_ChangeHash()
    {
        // The survival section is also a reason of its own for a world with nothing but harvested props (D15).
        var plain = new PropFixture(state: WorldFactory.CreateDefault());
        var harvested = new PropFixture(state: WorldFactory.CreateDefault());
        Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(plain.State));

        harvested.Apply(PropFixture.Harvest(PropFixture.Tree(0)));

        Assert.NotEqual(StateHasher.Hash(plain.State), StateHasher.Hash(harvested.State));
    }

    [Fact]
    public void HarvestThenRegrow_HashEqualsBefore()
    {
        // LastOpId is part of the hash, so the worlds that are compared have applied the same number of operations.
        var regrown = new PropFixture();
        var other = new PropFixture();
        var harvested = new PropFixture();
        var untouched = new PropFixture();
        regrown.Apply(PropFixture.Harvest(PropFixture.Tree(0), 100));
        regrown.Apply(PropFixture.Regrow(PropFixture.Tree(0)));
        other.Apply(PropFixture.Harvest(PropFixture.Rock(3), 5000));
        other.Apply(PropFixture.Regrow(PropFixture.Rock(3)));
        harvested.Apply(PropFixture.Harvest(PropFixture.Tree(0), 100));
        harvested.Apply(PropFixture.Harvest(PropFixture.Tree(1), 100));

        Assert.Equal(0, regrown.State.HarvestedProps.Count);
        Assert.Equal(StateHasher.Hash(other.State), StateHasher.Hash(regrown.State));
        Assert.NotEqual(StateHasher.Hash(untouched.State), StateHasher.Hash(harvested.State));
        Assert.NotEqual(StateHasher.Hash(regrown.State), StateHasher.Hash(harvested.State));
    }

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
    /// The K6-30 world of the functional block tests: two players with their containers and the start kit (a tool in the hotbar, a gem
    /// and stone in the bag), the functional block construct with a chest with a container and stone, a bed owned by player 1 with its
    /// respawn point and an open door. No harvested props.
    /// </summary>
    private static WorldState WorldWithBlockEntities()
    {
        var f = new FunctionalBlockFixture(players: 2);
        f.Apply(ItemFixture.Create(new SlotRef(FunctionalBlockFixture.Hotbar, 0), ItemFixture.Tool, 1, limit: 1, source: ItemSource.Starter));
        f.Apply(ItemFixture.Create(new SlotRef(FunctionalBlockFixture.Hotbar, 1), ItemFixture.Gem, 3, limit: 5, source: ItemSource.Starter));
        f.Apply(ItemFixture.Create(new SlotRef(FunctionalBlockFixture.Bag, 0), ItemFixture.Stone, 40, source: ItemSource.Harvest));
        var chest = f.SetUpChest(FunctionalBlocks.ChestCell, size: 8);
        f.Give(chest, 2, 10);
        f.SetUpBed(FunctionalBlocks.BedCell, FunctionalBlockFixture.Player1);
        f.Apply(FunctionalBlockFixture.SetFunctionState(FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1));
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

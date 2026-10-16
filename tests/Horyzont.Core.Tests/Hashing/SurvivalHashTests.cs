using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Progress;
using Horyzont.Core.Tests.BlockEntities;
using Horyzont.Core.Tests.Items;
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

    [Fact]
    public void WorldWithoutItems_HashUnchanged() => Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(WorldFactory.CreateDefault()));

    [Fact]
    public void WorldWithItemsWithoutBlockEntities_HashUnchanged() =>
        Assert.Equal(WorldWithItemsHashBeforeBlockEntities, StateHasher.Hash(WorldWithItems()));

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
        chestB.CreateChestContainer(FunctionalBlocks.ChestCell);
        Assert.NotEqual(StateHasher.Hash(chestB.State), StateHasher.Hash(chestA.State));
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

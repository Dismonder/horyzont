using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Items;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Hashing;

/// <summary>D7 of K6-29W: the survival section is appended only when the world has item types, containers or a moved container counter.</summary>
public sealed class SurvivalHashTests
{
    /// <summary>Hash of <see cref="WorldFactory.CreateDefault"/> recorded at commit eee8f17 (before stage A of K6-29W).</summary>
    private const ulong DefaultWorldHashBeforeItems = 17523897430694564880UL;

    [Fact]
    public void WorldWithoutItems_HashUnchanged() => Assert.Equal(DefaultWorldHashBeforeItems, StateHasher.Hash(WorldFactory.CreateDefault()));

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

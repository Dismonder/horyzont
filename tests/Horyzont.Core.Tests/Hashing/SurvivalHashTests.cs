using Horyzont.Core.Checkpoints;
using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
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

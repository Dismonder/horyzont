using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World;

public sealed class RegistryTests
{
    [Fact]
    public void CreateNew_NumbersFromOne_AppendsMissing()
    {
        var state = WorldFactory.CreateDefault();

        Assert.Equal([new RegistryEntry(1, "core:stone_block"), new RegistryEntry(2, BlockTypeRegistry.MissingKey)], state.BlockTypes.Entries);
        Assert.Equal([new RegistryEntry(1, "stone"), new RegistryEntry(2, "dirt")], state.Materials.Entries);
        Assert.True(state.BlockTypes.IsKnown(1));
        Assert.False(state.BlockTypes.IsKnown(0));
        Assert.False(state.Materials.IsKnown(0));
        Assert.Equal(Core.Ids.OpId.None, state.LastOpId);
        Assert.Equal(1UL, state.EntityIds.NextValue);
        Assert.Equal(1UL, state.PlayerIds.NextValue);
    }

    [Fact]
    public void CreateNew_MissingGiven_NotDuplicated()
    {
        var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], [BlockTypeRegistry.MissingKey, "a"], []);

        Assert.Equal([new RegistryEntry(1, BlockTypeRegistry.MissingKey), new RegistryEntry(2, "a")], state.BlockTypes.Entries);
    }

    [Fact]
    public void CreateNew_TooManyBlockTypes_Throws()
    {
        var keys = Enumerable.Range(0, ProtocolConstants.MaxBlockTypes).Select(i => $"b:{i}");

        Assert.Throws<InvalidOperationException>(() => WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], keys, []));
    }

    [Fact]
    public void CreateNew_TooManyMaterials_Throws()
    {
        var keys = Enumerable.Range(0, ProtocolConstants.MaxMaterials + 1).Select(i => $"m:{i}");

        Assert.Throws<InvalidOperationException>(() => WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], ["a"], keys));
    }

    [Fact]
    public void CreateNew_MaxMaterials_Fit()
    {
        var keys = Enumerable.Range(0, ProtocolConstants.MaxMaterials).Select(i => $"m:{i}");

        var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], ["a"], keys);

        Assert.Equal(255, state.Materials.Entries[^1].Id);
    }

    [Fact]
    public void CreateNew_KeyTooLong_Throws()
    {
        Assert.Throws<InvalidDataException>(() => WorldState.CreateNew(WorldFactory.DefaultWorldId, 1, [WorldFactory.HomePlanetParams], [new string('x', 65)], []));
    }
}

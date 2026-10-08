using Horyzont.Core.Protocol;
using Horyzont.Core.World;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class RegistryGrowthTests
{
    // Without planets the world needs no blob codec: the registries alone are under test.
    private static string CreatePlanetless(TempDirectory dir)
    {
        var world = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [], [WorldFactory.StoneBlockKey], ["stone", "dirt"]);
        WorldCreator.Create(dir.WorldPath, world, WorldHarness.Options, new FakeTimeProvider());
        return dir.WorldPath;
    }

    private static LoadedWorld Load(string path, IReadOnlyList<string>? blockTypes = null, IReadOnlyList<string>? materials = null) =>
        WorldLoader.Load(
            path,
            WorldHarness.LoadOptions(path) with { BlockTypeKeys = blockTypes ?? [], MaterialKeys = materials ?? [] },
            new FakeTimeProvider(),
            NullLogger.Instance);

    [Fact]
    public void NewBlockTypeKey_GetsNextNumber_AndStaysAfterReload()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        var first = Load(path, [WorldFactory.StoneBlockKey, "core:glass", "core:wood"], ["stone", "sand"]);

        Assert.True(first.State.BlockTypes.TryGetId("core:glass", out var glass));
        Assert.True(first.State.BlockTypes.TryGetId("core:wood", out var wood));
        Assert.Equal(3, glass);
        Assert.Equal(4, wood);
        Assert.True(first.State.Materials.TryGetId("sand", out var sand));
        Assert.Equal(3, sand);

        var second = Load(path);
        Assert.True(second.State.BlockTypes.TryGetId("core:glass", out var glassAgain));
        Assert.Equal(glass, glassAgain);
        Assert.True(second.State.Materials.TryGetId("sand", out var sandAgain));
        Assert.Equal(sand, sandAgain);
    }

    [Fact]
    public void KeyInDatabaseMissingFromOptions_KeepsItsNumber()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);

        var loaded = Load(path, ["core:other"], ["dirt"]);

        Assert.True(loaded.State.BlockTypes.TryGetId(WorldFactory.StoneBlockKey, out var stone));
        Assert.Equal(1, stone);
        Assert.True(loaded.State.BlockTypes.TryGetId("core:other", out var other));
        Assert.Equal(3, other);
        Assert.True(loaded.State.Materials.TryGetId("stone", out var material));
        Assert.Equal(1, material);
    }

    [Fact]
    public void BlockTypesOverMax_ThrowsInvalidOperation_AndAddsNothing()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);
        var keys = Enumerable.Range(0, ProtocolConstants.MaxBlockTypes - 1).Select(i => $"core:block_{i}").ToList();

        Assert.Throws<InvalidOperationException>(() => Load(path, keys));

        Assert.Equal(2L, RawDb.Long(path, "SELECT COUNT(*) FROM block_types;"));
        var exactlyFull = Load(path, keys.Take(ProtocolConstants.MaxBlockTypes - 2).ToList());
        Assert.Equal(ProtocolConstants.MaxBlockTypes, exactlyFull.State.BlockTypes.Entries.Count);
    }

    [Fact]
    public void MaterialsOverMax_ThrowsInvalidOperation()
    {
        using var dir = new TempDirectory();
        var path = CreatePlanetless(dir);
        var keys = Enumerable.Range(0, ProtocolConstants.MaxMaterials - 1).Select(i => $"m{i}").ToList();

        Assert.Throws<InvalidOperationException>(() => Load(path, materials: keys));
    }
}

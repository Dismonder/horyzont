using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Core.World.Blocks;
using Horyzont.Game.Planet;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

[TestSuite]
public class TerrainMaterialTests
{
    private static MaterialRegistry RegistryOf(params string[] keys)
    {
        var planet = PlanetGenesis.CreatePlanet(1, 0, "core:planet/1");
        return WorldState.CreateNew(new WorldId(Guid.Empty), 1, [planet], [], keys).Materials;
    }

    [TestCase]
    public void MaterialMap_PermutedRegistry_MapsByKey()
    {
        var registry = RegistryOf(
            TerrainMaterials.CoreMaterialKey, TerrainMaterials.OreKey, TerrainMaterials.StoneKey, TerrainMaterials.SandKey, TerrainMaterials.DirtKey);
        var map = TerrainMaterialMap.Build(registry);

        foreach (var (key, textureIndex) in TerrainMaterials.All)
        {
            AssertThat(registry.TryGetId(key, out var id)).IsTrue();
            AssertThat((int)map.ToTextureIndex(id)).IsEqual(textureIndex);
        }

        // The numbers of the permuted registry differ from the texture indices, so the mapping really went through the key.
        AssertThat(registry.TryGetId(TerrainMaterials.StoneKey, out var stoneId)).IsTrue();
        AssertThat((int)stoneId).IsNotEqual(0);
    }

    [TestCase]
    public void MaterialMap_UnknownKey_Throws()
    {
        var registry = RegistryOf(TerrainMaterials.StoneKey, "mod:unobtainium");

        AssertThrown(() => TerrainMaterialMap.Build(registry))
            .IsInstanceOf<InvalidDataException>()
            .StartsWithMessage("The world uses the terrain material 'mod:unobtainium'");
    }

    [TestCase]
    public void MaterialMap_RoundTrip()
    {
        var registry = RegistryOf(TerrainMaterials.Keys.ToArray());
        var map = TerrainMaterialMap.Build(registry);

        foreach (var entry in registry.Entries)
        {
            var textureIndex = map.ToTextureIndex((byte)entry.Id);
            AssertThat(map.TryGetMaterialId(textureIndex, out var materialId)).IsTrue();
            AssertThat((int)materialId).IsEqual(entry.Id);
        }
    }

    [TestCase]
    public void MaterialMap_UnknownMaterialId_Throws()
    {
        var map = TerrainMaterialMap.Build(RegistryOf(TerrainMaterials.StoneKey));

        AssertThrown(() => map.ToTextureIndex(0)).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThrown(() => map.ToTextureIndex(9)).IsInstanceOf<ArgumentOutOfRangeException>();
        AssertThat(map.TryGetMaterialId(TerrainMaterials.DirtTextureIndex, out _)).IsFalse();
    }

    [TestCase]
    public void TerrainMaterials_IndicesFrozen()
    {
        // Append only: the block data in saved worlds stores these indices.
        (string, byte)[] frozen =
        [
            ("core:stone", 0),
            ("core:dirt", 1),
            ("core:sand", 2),
            ("core:ore", 3),
            ("core:bedrock", 4),
        ];

        AssertThat(TerrainMaterials.All.ToArray()).IsEqual(frozen);
        AssertThat(TerrainMaterials.Keys.ToArray()).IsEqual(frozen.Select(f => f.Item1).ToArray());
    }

    [TestCase]
    [RequireGodotRuntime]
    public void TerrainChannels_Format_SetsDepthsAndMask()
    {
        var format = TerrainChannels.CreateFormat();

        AssertThat(format.GetChannelDepth(TerrainChannels.Sdf)).IsEqual(Godot.VoxelBuffer.Depth.Depth16Bit);
        AssertThat(format.GetChannelDepth(TerrainChannels.Material)).IsEqual(Godot.VoxelBuffer.Depth.Depth8Bit);
        AssertThat(TerrainChannels.UsedChannelsMask).IsEqual(0b1010);
    }
}

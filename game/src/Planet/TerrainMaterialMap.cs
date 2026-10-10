using Horyzont.Core.World.Blocks;

namespace Horyzont.Game.Planet;

/// <summary>
/// Maps the material ids of a world (numbers of its <see cref="MaterialRegistry"/>, which differ between worlds) to the texture
/// indices stored in terrain voxels, always through the material key.
/// </summary>
public sealed class TerrainMaterialMap
{
    private readonly byte[] _toTexture = new byte[256];
    private readonly bool[] _known = new bool[256];
    private readonly byte[] _toMaterial = new byte[256];
    private readonly bool[] _textureKnown = new bool[256];

    private TerrainMaterialMap()
    {
    }

    /// <summary>Builds the map from the registry of a world.</summary>
    /// <exception cref="InvalidDataException">A key of the registry is not in <see cref="TerrainMaterials"/> (a save from a newer game version).</exception>
    public static TerrainMaterialMap Build(MaterialRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return Build(registry.Entries);
    }

    /// <summary>Builds the map from the material entries of a world (the server hello carries them).</summary>
    /// <exception cref="InvalidDataException">A key of the entries is not in <see cref="TerrainMaterials"/> (a server of a newer game version).</exception>
    public static TerrainMaterialMap Build(IEnumerable<RegistryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var map = new TerrainMaterialMap();
        foreach (var entry in entries)
        {
            var textureIndex = -1;
            foreach (var (key, index) in TerrainMaterials.All)
            {
                if (key == entry.Key)
                {
                    textureIndex = index;
                    break;
                }
            }

            if (textureIndex < 0)
            {
                throw new InvalidDataException($"The world uses the terrain material '{entry.Key}' which this game version does not know.");
            }

            map._toTexture[entry.Id] = (byte)textureIndex;
            map._known[entry.Id] = true;
            map._toMaterial[textureIndex] = (byte)entry.Id;
            map._textureKnown[textureIndex] = true;
        }

        return map;
    }

    /// <summary>Texture index to store in a voxel for a material of the world.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The id is not in the registry of the world.</exception>
    public byte ToTextureIndex(byte materialId) =>
        _known[materialId] ? _toTexture[materialId] : throw new ArgumentOutOfRangeException(nameof(materialId), materialId, "Unknown material id.");

    /// <summary>Material of the world for a texture index; false when the world does not register that material.</summary>
    public bool TryGetMaterialId(byte textureIndex, out byte materialId)
    {
        materialId = _toMaterial[textureIndex];
        return _textureKnown[textureIndex];
    }
}

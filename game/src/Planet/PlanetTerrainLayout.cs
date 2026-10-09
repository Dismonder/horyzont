using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;
using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.Planet;

/// <summary>Voxel bounds, LOD count and block sizes of planet terrain.</summary>
public static class PlanetTerrainLayout
{
    /// <summary>Edge of a data block in voxels; equals <see cref="TerrainMath.BlockSize"/>.</summary>
    public const int DataBlockSize = 16;

    /// <summary>Edge of a mesh block in voxels (visual only; tuned in K4-19).</summary>
    public const int MeshBlockSize = 16;

    /// <summary>Half edge of the cubic voxel bounds around the planet centre: ceil16(R + MaxRelief + 64).</summary>
    public static int HalfExtentMeters(PlanetParams planet)
    {
        ArgumentNullException.ThrowIfNull(planet);
        var extent = (int)MathF.Ceiling(planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + 64f);
        return (extent + DataBlockSize - 1) / DataBlockSize * DataBlockSize;
    }

    /// <summary>Number of LODs: the coarsest block must cover the whole diameter.</summary>
    public static int LodCount(PlanetParams planet)
    {
        var diameterBlocks = 2 * HalfExtentMeters(planet) / DataBlockSize;
        var lods = 1;
        while ((1 << (lods - 1)) < diameterBlocks)
        {
            lods++;
        }

        return lods;
    }
}

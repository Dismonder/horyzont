using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Tests.World.Terrain;

public sealed class TerrainMathTests
{
    private static readonly PlanetId _planet = new(1);

    [Fact]
    public void AffectedBlocks_OnBlockBoundary_IncludesNeighbours()
    {
        var output = new List<TerrainBlockKey>();

        TerrainMath.AffectedBlocks(_planet, new Vector3(16f, 8f, 8f), 0.5f, 1f, 0, output);

        Assert.Equal([new TerrainBlockKey(_planet, 0, 0, 0, 0), new TerrainBlockKey(_planet, 0, 1, 0, 0)], output);
    }

    [Fact]
    public void AffectedBlocks_MarginAndVoxelSize_GrowTheBox()
    {
        var output = new List<TerrainBlockKey>();

        TerrainMath.AffectedBlocks(_planet, new Vector3(8f, 8f, 8f), 1f, 0.5f, 4, output);

        // Blocks are 8 m with 0.5 m voxels; 8 ± 3 m spans block 0 and 1 on every axis.
        Assert.Equal(8, output.Count);
    }

    [Fact]
    public void AffectedBlocks_Result_IsSortedAndCleared()
    {
        var output = new List<TerrainBlockKey> { new(_planet, 9, 9, 9, 9) };

        TerrainMath.AffectedBlocks(_planet, new Vector3(-1f, 31.5f, 0f), 2f, 1f, 4, output);

        Assert.DoesNotContain(new TerrainBlockKey(_planet, 9, 9, 9, 9), output);
        Assert.Equal(output.Order().ToList(), output);
        Assert.Equal(output.Distinct().Count(), output.Count);
    }

    [Fact]
    public void AffectedBlocks_NegativeCoordinates_UseFloor()
    {
        var output = new List<TerrainBlockKey>();

        TerrainMath.AffectedBlocks(_planet, new Vector3(-1f, -1f, -1f), 0.25f, 1f, 0, output);

        Assert.Equal([new TerrainBlockKey(_planet, 0, -1, -1, -1)], output);
    }

    [Fact]
    public void UpAt_Normalizes_AndDefaultsAtCenter()
    {
        Assert.Equal(Vector3.UnitY, TerrainMath.UpAt(Vector3.Zero));
        Assert.Equal(1f, TerrainMath.UpAt(new Vector3(3f, -4f, 12f)).Length(), 5);
    }
}

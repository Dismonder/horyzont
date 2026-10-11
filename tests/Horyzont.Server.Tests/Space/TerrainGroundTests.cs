using System.Numerics;

using Horyzont.Server.Space;

namespace Horyzont.Server.Tests.Space;

public sealed class TerrainGroundTests
{
    private const float R = 4000f;
    private const float Relief = 300f;

    [Fact]
    public void TerrainGround_MountaintopDescent_ViewerAndReadinessUseTheSameGroundPoint()
    {
        // A fast ship high over a mountaintop: the far column ends just above the summit (within the 40 m core).
        var up = Vector3.Normalize(new Vector3(0.2f, 1f, -0.1f));
        var summit = R + Relief - 5f;
        var column = TerrainColumn.UnderShip(up * (summit + 334f), 10f, 120f, summit + 334f - (R - Relief), R, Relief);
        var bottom = column.Center.Length() - column.Radius;
        Assert.InRange(bottom - summit, 0.1f, TerrainGround.CoreMeters);

        Assert.True(TerrainGround.TryGroundPoint(column.Center, column.Radius, summit, out var readinessPoint));
        var viewerPoint = TerrainGround.ViewerPoint(column, summit);

        Assert.Equal(summit, readinessPoint.Length(), 2);
        Assert.Equal(readinessPoint, viewerPoint);
    }

    [Fact]
    public void TerrainGround_GroundOutsideSpanPlusCore_ViewerAtColumnCentre()
    {
        var column = new TerrainColumn(new Vector3(0f, R + 1000f, 0f), 50f, 100f);

        Assert.False(TerrainGround.TryGroundPoint(column.Center, column.Radius, R + 1000f - 50f - 40f - 1f, out _));
        Assert.True(TerrainGround.TryGroundPoint(column.Center, column.Radius, R + 1000f - 50f - 39f, out _));
        Assert.False(TerrainGround.TryGroundPoint(column.Center, column.Radius, float.NaN, out _));
        Assert.Equal(column.Center, TerrainGround.ViewerPoint(column, R));
    }

    [Fact]
    public void TerrainGround_Core_IsQueryRadiusUpTo40m()
    {
        Assert.Equal(8f, TerrainGround.Core(8f));
        Assert.Equal(40f, TerrainGround.Core(400f));
        Assert.Equal(0f, TerrainGround.Core(-1f));
    }
}

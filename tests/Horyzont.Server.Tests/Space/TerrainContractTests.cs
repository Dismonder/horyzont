using System.Numerics;

using Horyzont.Server.Space;
using Horyzont.Server.Tests.Frames;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Space;

public sealed class TerrainContractTests
{
    private const float R = 4000f;
    private const float Relief = 300f;

    [Fact]
    public void TerrainColumn_FastHighShip_LooksAhead2sPlus64m()
    {
        var ship = new Vector3(0f, R + 5000f, 0f);

        var column = TerrainColumn.UnderShip(ship, 10f, 100f, 5000f, R, Relief);

        Assert.Equal(264f, column.Depth, 3);
        Assert.Equal(R + 5000f - 132f, column.Center.Y, 2);
        Assert.Equal(0f, column.Center.X, 3);
        Assert.Equal(10f + 132f, column.Radius, 3);
        Assert.Equal(column.Radius + 64f, column.ViewerDistance, 3);
    }

    [Fact]
    public void TerrainColumn_LowShip_DepthIsAltitude_CentreNotBelowRelief()
    {
        var ship = Vector3.Normalize(new Vector3(1f, 1f, 0f)) * (R + Relief + 40f);

        var column = TerrainColumn.UnderShip(ship, 8f, 500f, 40f, R, Relief);

        Assert.Equal(40f, column.Depth, 3);
        Assert.Equal(R + Relief + 20f, column.Center.Length(), 2);
        Assert.Equal(1f, Vector3.Dot(Vector3.Normalize(column.Center), Vector3.Normalize(ship)), 4);
        Assert.Equal(28f, column.Radius, 3);
    }

    [Fact]
    public void TerrainColumn_ShipInValley_SpansFromLowestGroundToShip()
    {
        // r < R + MaxRelief: the ground under the ship may lie anywhere down to R − MaxRelief (owner decision, README Y11).
        var ship = new Vector3(0f, R + 100f, 0f);

        var column = TerrainColumn.UnderShip(ship, 8f, 0f, 100f, R, Relief);

        Assert.Equal(R - 100f, column.Center.Y, 2);
        Assert.Equal(200f + 8f, column.Radius, 3);
        Assert.True(column.Center.Y - column.Radius <= R - Relief);
        Assert.True(column.Center.Y + column.Radius >= R + 100f);
    }

    [Fact]
    public void TerrainColumn_ShipJustAboveRelief_ColumnStillReachesLowestGround()
    {
        // r − d/2 ≤ R + MaxRelief although the ship is above the relief: the column spans from R − MaxRelief up to r.
        var ship = new Vector3(R + Relief + 40f, 0f, 0f);

        var column = TerrainColumn.UnderShip(ship, 8f, 500f, 200f, R, Relief);

        Assert.Equal(R + 20f, column.Center.X, 2);
        Assert.Equal(Relief + 20f + 8f, column.Radius, 3);
        Assert.True(column.Center.X - column.Radius <= R - Relief);
    }

    [Fact]
    public void TerrainColumn_ShipHighAbove_KeepsFarShipColumn()
    {
        var ship = new Vector3(0f, 0f, R + 20000f);

        var column = TerrainColumn.UnderShip(ship, 12f, 2000f, 20000f, R, Relief);

        Assert.Equal(4064f, column.Depth, 3);
        Assert.Equal(R + 20000f - 2032f, column.Center.Z, 1);
        Assert.Equal(12f + 2032f, column.Radius, 3);
        Assert.True(column.Center.Z - column.Radius > R + Relief);
    }

    [Fact]
    public void MovingShipDemand_MovingShipDemands_ParkedDoesNot()
    {
        var rig = new FrameRig();
        var demand = new MovingShipDemand(rig.History);
        Assert.True(rig.State.Constructs.TryGet(TestShips.Moving, out var moving));
        Assert.True(rig.State.Constructs.TryGet(TestShips.Parked, out var parked));
        Assert.False(demand.Demands(moving));

        rig.Tick(40);

        Assert.True(demand.Demands(moving));
        Assert.False(demand.Demands(parked));
    }
}

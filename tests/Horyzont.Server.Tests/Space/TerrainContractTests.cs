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

        var below = TerrainColumn.UnderShip(new Vector3(0f, R + 100f, 0f), 8f, 0f, 100f, R, Relief);
        Assert.Equal(R + Relief, below.Center.Y, 2);
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

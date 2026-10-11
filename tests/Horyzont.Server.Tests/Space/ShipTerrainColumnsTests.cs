using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Space;
using Horyzont.Server.Tests.Frames;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Space;

public sealed class ShipTerrainColumnsTests
{
    private const float Relief = 300f;

    [Fact]
    public void ServerShipViewers_ColumnUnderLowShip_ReachesLowestGround()
    {
        var rig = new FrameRig();
        rig.Tick(5);
        Assert.True(rig.State.Constructs.TryGet(TestShips.Moving, out var ship));
        Assert.True(rig.State.Planets.TryGet(new PlanetId(TestShips.HomeFrame.Id), out var home));

        Assert.True(ShipTerrainColumns.TryGet(ship, rig.State, rig.History, rig.TickSeconds, Relief, out var planet, out var column));

        Assert.Equal(home.Id, planet);
        var r = ship.LocalToParent(ShipTerrainColumns.BoundingCenter(ship)).Length();
        Assert.True(column.Center.Length() - column.Radius <= home.RadiusMeters - Relief + 0.01f);
        Assert.True(column.Center.Length() + column.Radius >= r);
    }

    [Fact]
    public void ServerShipViewers_FarShip_UsesLookaheadColumn()
    {
        var state = TestShips.CreateWorld(new TestShip(TestShips.Moving, TestShips.HomeFrame, new Vector3(0f, 9000f, 0f), Quaternion.Identity));
        var rig = new FrameRig(state);
        Assert.True(state.Constructs.TryGet(TestShips.Moving, out var ship));
        TestShips.ForceMode(ship, ConstructMode.Ship);

        Assert.True(ShipTerrainColumns.TryGet(ship, state, rig.History, rig.TickSeconds, Relief, out _, out var column));

        var hull = ShipTerrainColumns.BoundingRadius(ship);
        var r = ship.LocalToParent(ShipTerrainColumns.BoundingCenter(ship)).Length();
        Assert.Equal(TerrainColumn.MarginMeters, column.Depth, 3);
        Assert.Equal(r - 32f, column.Center.Length(), 1);
        Assert.Equal(hull + 32f, column.Radius, 3);
    }

    [Fact]
    public void ServerShipViewers_StaticConstruct_NoColumn()
    {
        var rig = new FrameRig();
        Assert.True(rig.State.Constructs.TryGet(TestShips.Parked, out var parked));
        TestShips.ForceMode(parked, ConstructMode.Static);

        Assert.False(ShipTerrainColumns.TryGet(parked, rig.State, rig.History, rig.TickSeconds, Relief, out _, out _));
    }
}

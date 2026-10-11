using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Server.Space;

namespace Horyzont.Server.Tests.Space;

public sealed class ShipTerrainQueryTests
{
    private const float R = 4000f;
    private const float Relief = 300f;
    private static readonly PlanetId _planet = new(2);

    [Fact]
    public void TerrainSweep_Ahead_OffsetIs2sOfSpeedUpTo40m()
    {
        var ship = new Vector3(0f, R, 0f);

        Assert.Equal(ship + new Vector3(10f, 0f, 0f), TerrainSweep.Ahead(ship, 6f, new Vector3(5f, 0f, 0f)).Center);
        Assert.Equal(ship + new Vector3(0f, 0f, -40f), TerrainSweep.Ahead(ship, 6f, new Vector3(0f, 0f, -300f)).Center);
        Assert.Equal(ship, TerrainSweep.Ahead(ship, 6f, Vector3.Zero).Center);
        Assert.Equal(14f, TerrainSweep.Ahead(ship, 6f, Vector3.Zero).HalfSize);
    }

    [Fact]
    public void TerrainSweep_MaxHorizontalSpeed_KeepsTheShipInTheLod0RingBetweenRefreshes()
    {
        Assert.Equal((96f - 40f - 10f) * 6f, TerrainSweep.MaxHorizontalSpeed(10f), 3);
        Assert.Equal(0f, TerrainSweep.MaxHorizontalSpeed(60f));
        Assert.Equal(56f, TerrainSweep.FullBoxMaxRadius);
    }

    [Fact]
    public void ShipTerrainQuery_FarShip_NoSweep_LowShip_Sweep()
    {
        var far = ShipTerrainQuery.Of(new Vector3(0f, R + 5000f, 0f), 10f, new Vector3(0f, -100f, 0f), 5000f + Relief, R, Relief);
        var low = ShipTerrainQuery.Of(new Vector3(0f, R + Relief + 100f, 0f), 10f, new Vector3(20f, 0f, 0f), 100f + (2f * Relief), R, Relief);

        Assert.False(far.HasSweep);
        Assert.True(low.HasSweep);

        // r − hull − d < R + MaxRelief + 64 is the band edge.
        Assert.False(TerrainSweep.InBand(new Vector3(0f, R + Relief + 64f + 10f + 50f, 0f), 10f, 50f, R, Relief));
        Assert.True(TerrainSweep.InBand(new Vector3(0f, R + Relief + 63f + 10f + 50f, 0f), 10f, 50f, R, Relief));
    }

    [Fact]
    public void ShipTerrainQuery_SidewaysAlongValleyWall_ReadyOnlyOnceTheSweepBoxIsBuilt()
    {
        // A ship 350 m over a valley floor flying sideways at hull height towards a ridge: the ground viewer on the floor is far from it.
        var up = Vector3.UnitY;
        var floor = R - 200f;
        var ship = up * (floor + 350f);
        var query = ShipTerrainQuery.Of(ship, 10f, new Vector3(30f, 0f, 0f), ship.Length() - (R - Relief), R, Relief);
        var groundViewer = TerrainGround.ViewerPoint(query.Column, floor);

        Assert.True(query.HasSweep);
        Assert.Equal(floor, groundViewer.Length(), 2);
        Assert.True(query.NeedsSweepViewer(groundViewer));
        Assert.Equal(ship + new Vector3(40f, 0f, 0f), query.Sweep.Center);

        // The ground under the ship is built (the old rule said Ready), the box at hull height is not.
        var terrain = new FakeReadiness();
        terrain.Built.Add((groundViewer, TerrainGround.CoreMeters));
        Assert.Equal(PlanetTerrainReadiness.Streaming, terrain.GetShipReadiness(_planet, query));

        terrain.Built.Add((query.Sweep.Center, query.Sweep.HalfSize));
        Assert.Equal(PlanetTerrainReadiness.Ready, terrain.GetShipReadiness(_planet, query));
    }

    [Fact]
    public void ShipTerrainQuery_DeepPlayerShaft_SweepViewerStandsAtTheShip()
    {
        // A ship 150 m down a shaft dug by players: the generated ground is above the whole column, so only the sweep box applies.
        var up = Vector3.Normalize(new Vector3(1f, 2f, 3f));
        var generated = R + 20f;
        var ship = up * (generated - 150f);
        var query = ShipTerrainQuery.Of(ship, 6f, up * -4f, ship.Length() - (R - Relief), R, Relief);

        Assert.False(TerrainGround.TryGroundPoint(query.Column.Center, query.Column.Radius, generated, out _));
        var groundViewer = TerrainGround.ViewerPoint(query.Column, generated);
        Assert.True(query.HasSweep);
        Assert.True(query.NeedsSweepViewer(groundViewer));
        Assert.True(Vector3.Distance(query.Sweep.Center, ship) <= 8f + 1e-3f);

        var terrain = new FakeReadiness { GroundReady = true };
        Assert.Equal(PlanetTerrainReadiness.Streaming, terrain.GetShipReadiness(_planet, query));
        terrain.Built.Add((query.Sweep.Center, query.Sweep.HalfSize));
        Assert.Equal(PlanetTerrainReadiness.Ready, terrain.GetShipReadiness(_planet, query));
    }

    [Fact]
    public void ShipTerrainQuery_LowOverGround_OneViewerSuffices()
    {
        var floor = R + 10f;
        var ship = new Vector3(0f, floor + 30f, 0f);
        var query = ShipTerrainQuery.Of(ship, 6f, new Vector3(0f, -5f, 0f), ship.Length() - (R - Relief), R, Relief);

        Assert.True(query.HasSweep);
        Assert.False(query.NeedsSweepViewer(TerrainGround.ViewerPoint(query.Column, floor)));
    }

    [Fact]
    public void GetShipReadiness_ColumnSizedQuery_AsksTheColumnAndTheSweepBox()
    {
        var ship = new Vector3(0f, R + 100f, 0f);
        var query = ShipTerrainQuery.Of(ship, 10f, new Vector3(0f, -20f, 0f), ship.Length() - (R - Relief), R, Relief);
        var terrain = new FakeReadiness { GroundReady = true };
        terrain.Built.Add((query.Sweep.Center, query.Sweep.HalfSize));

        Assert.Equal(PlanetTerrainReadiness.Ready, terrain.GetShipReadiness(_planet, query));

        Assert.Equal(2, terrain.Queries.Count);
        Assert.Equal((query.Column.Center, query.Column.Radius), terrain.Queries[0]);
        Assert.True(query.Column.Radius > TerrainSweep.FullBoxMaxRadius);
        Assert.Equal((query.Sweep.Center, query.Sweep.HalfSize), terrain.Queries[1]);
        Assert.True(query.Sweep.HalfSize <= TerrainSweep.FullBoxMaxRadius);
    }

    [Fact]
    public void Worse_FaultedThenInactiveThenStreamingThenReady()
    {
        Assert.Equal(PlanetTerrainReadiness.Faulted, PlanetTerrainReadinessQueries.Worse(PlanetTerrainReadiness.Inactive, PlanetTerrainReadiness.Faulted));
        Assert.Equal(PlanetTerrainReadiness.Inactive, PlanetTerrainReadinessQueries.Worse(PlanetTerrainReadiness.Streaming, PlanetTerrainReadiness.Inactive));
        Assert.Equal(PlanetTerrainReadiness.Streaming, PlanetTerrainReadinessQueries.Worse(PlanetTerrainReadiness.Ready, PlanetTerrainReadiness.Streaming));
        Assert.Equal(PlanetTerrainReadiness.Ready, PlanetTerrainReadinessQueries.Worse(PlanetTerrainReadiness.Ready, PlanetTerrainReadiness.Ready));
    }

    /// <summary>Ready for a small sphere only when built; a column-sized sphere is ready when its ground is (or <see cref="GroundReady"/>).</summary>
    private sealed class FakeReadiness : IPlanetTerrainReadiness
    {
        public List<(Vector3 Center, float Radius)> Built { get; } = [];

        public List<(Vector3 Center, float Radius)> Queries { get; } = [];

        public bool GroundReady { get; init; }

        public PlanetTerrainReadiness GetReadiness(PlanetId planet, Vector3 localCenter, float radius)
        {
            Queries.Add((localCenter, radius));
            if (radius > TerrainSweep.FullBoxMaxRadius)
            {
                return GroundReady || Built.Count > 0 ? PlanetTerrainReadiness.Ready : PlanetTerrainReadiness.Streaming;
            }

            return Built.Contains((localCenter, radius)) ? PlanetTerrainReadiness.Ready : PlanetTerrainReadiness.Streaming;
        }
    }
}

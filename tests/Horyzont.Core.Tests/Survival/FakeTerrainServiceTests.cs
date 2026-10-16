using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

public sealed class FakeTerrainServiceTests
{
    [Fact]
    public void CaveExitAboveSurface_ContinuesAsOutsideRay()
    {
        var terrain = new FakeTerrainService(100f);
        terrain.Cavities.Add(new TerrainCavity(new Vector3(0f, 99f, 0f), 5f));
        var planet = new PlanetId(1);
        var origin = new Vector3(0f, 99f, 0f);
        Assert.True(terrain.Raycast(planet, origin, -Vector3.UnitY, 16f, out var wall));
        Assert.Equal(5f, wall.Distance);
        Assert.False(terrain.Raycast(planet, origin, Vector3.UnitY, 16f, out _));
        Assert.False(terrain.Raycast(planet, new Vector3(0f, 104.001f, 0f), Vector3.UnitY, 10.999f, out _));
    }
}

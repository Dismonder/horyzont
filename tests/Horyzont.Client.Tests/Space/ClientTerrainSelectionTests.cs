using Horyzont.Client.Space;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;

namespace Horyzont.Client.Tests.Space;

public sealed class ClientTerrainSelectionTests
{
    private static readonly BodyInfo _home = new(new PlanetId(1), 0, Vector3D.Zero, 4000, 9.81);
    private static readonly BodyInfo _moon = new(new PlanetId(2), 1, new Vector3D(60_000, 0, 0), 1000, 1.6);
    private static readonly BodyInfo _far = new(new PlanetId(3), 2, new Vector3D(0, 0, 108_000), 3000, 7);
    private static readonly BodyInfo[] _bodies = [_home, _moon, _far];

    [Fact]
    public void ClientTerrainSelection_ActivatesAt4R_ReleasesBeyond4_4R()
    {
        var selection = new ClientTerrainSelection();

        selection.Update(At(_moon, 4.05), _bodies, default, 0);
        Assert.False(selection.HasActive);

        Assert.True(selection.Update(At(_moon, 3.99), _bodies, default, 1));
        Assert.Equal(_moon.Planet, selection.Active);

        selection.Update(At(_moon, 4.35), _bodies, default, 2);
        Assert.Equal(_moon.Planet, selection.Active);

        Assert.True(selection.Update(At(_moon, 4.45), _bodies, default, 3));
        Assert.False(selection.HasActive);
        Assert.Equal(_moon.Planet, selection.Fading);
    }

    [Fact]
    public void ClientTerrainSelection_NearestByRatioWins()
    {
        // Two bodies within 4 R of the camera (a synthetic layout): the smaller r / R wins and replaces the active one.
        var a = new BodyInfo(new PlanetId(7), 0, Vector3D.Zero, 1000, 1);
        var b = new BodyInfo(new PlanetId(8), 1, new Vector3D(6000, 0, 0), 2000, 1);
        BodyInfo[] bodies = [a, b];
        var selection = new ClientTerrainSelection();

        selection.Update(new Vector3D(3500, 0, 0), bodies, default, 0);
        Assert.Equal(b.Planet, selection.Active);

        selection.Update(new Vector3D(1500, 0, 0), bodies, default, 1);
        Assert.Equal(a.Planet, selection.Active);
        Assert.Equal(b.Planet, selection.Fading);
    }

    [Fact]
    public void ClientTerrainSelection_AtMostOneActivePlusFading()
    {
        var selection = new ClientTerrainSelection();
        selection.Update(At(_home, 1.5), _bodies, default, 0);
        Assert.Equal(_home.Planet, selection.Active);

        // The own entity is placed on the moon: forced at once, home fades for 10 s.
        selection.Update(At(_home, 1.5), _bodies, _moon.Planet, 1);
        Assert.Equal(_moon.Planet, selection.Active);
        Assert.Equal(_home.Planet, selection.Fading);

        selection.Update(At(_home, 1.5), _bodies, _far.Planet, 2);
        Assert.Equal(_far.Planet, selection.Active);
        Assert.Equal(_moon.Planet, selection.Fading);

        selection.Update(At(_home, 1.5), _bodies, _far.Planet, 11.9);
        Assert.Equal(_moon.Planet, selection.Fading);
        selection.Update(At(_home, 1.5), _bodies, _far.Planet, 12);
        Assert.False(selection.HasFading);
    }

    [Fact]
    public void ClientTerrainSelection_BeforeStarSystemState_HomeOnly()
    {
        var selection = new ClientTerrainSelection();

        selection.Update(At(_moon, 1.2), [_home], default, 0);
        Assert.False(selection.HasActive);

        selection.Update(At(_home, 1.01), [_home], _moon.Planet, 1);
        Assert.Equal(_home.Planet, selection.Active);
    }

    [Fact]
    public void ClientTerrainSelection_AllocatesZeroBytes()
    {
        var selection = new ClientTerrainSelection();
        selection.Update(At(_moon, 2), _bodies, default, 0);
        // Ignore isolated tiered JIT allocations; recurring allocations remain in every window.
        var smallest = long.MaxValue;
        for (var window = 0; window < 5 && smallest != 0; window++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 10_000; i++)
            {
                selection.Update(At(i % 2 == 0 ? _moon : _home, 2 + (i % 5)), _bodies, default, ((window * 10_000) + i) * 0.016);
            }

            smallest = Math.Min(smallest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, smallest);
    }

    private static Vector3D At(BodyInfo body, double radii) => body.Position + new Vector3D(0, radii * body.RadiusMeters, 0);
}

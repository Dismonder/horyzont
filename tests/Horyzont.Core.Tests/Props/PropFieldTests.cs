using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Props;

/// <summary>
/// <see cref="PropField"/> (K6-31 D6, D7): the layout of the generator profile placed on the generated surface that
/// <c>FakeTerrainService.GeneratedSurfaceRadius</c> reports. The fake surface is a sphere 5 m over the planet radius (inside every height band).
/// Every refusal test first finds a prop, because a field that never finds anything would pass them trivially.
/// </summary>
public sealed class PropFieldTests
{
    private static PlanetId Planet => PropWorldFixture.Planet;

    private static Vector3 Pole => new(0, PropWorldFixture.Surface, 0);

    private static List<PropInstance> Collect(PropField field, Vector3 center, float radius)
    {
        var found = new List<PropInstance>();
        field.CollectInSphere(Planet, center, radius, found);
        return found;
    }

    [Fact]
    public void TryGetProp_FlatSurface_AnchorOnSurface()
    {
        var f = new PropWorldFixture();
        var found = Collect(f.Field, Pole, 64f);

        Assert.NotEmpty(found);
        foreach (var instance in found.Take(40))
        {
            Assert.True(f.Field.TryGetProp(Planet, instance.Id, out var again), $"{instance.Id}");
            Assert.Equal(instance, again);
            Assert.NotEqual(PropKindV1.None, instance.Kind);
            Assert.Equal(instance.Kind is <= PropKindV1.Bush ? 0 : 1, instance.Id.Slot);
            Assert.Equal(PropWorldFixture.Surface, instance.Anchor.Length(), 0.01f);
            Assert.True(Vector3.Distance(Vector3.Normalize(instance.Anchor), instance.Up) < 1e-4f);
            Assert.InRange(instance.Scale, 0.4f, 2.2f);
        }
    }

    [Fact]
    public void TryGetProp_NoSurface_False()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        Assert.True(new PropField(f.State, f.Terrain).TryGetProp(Planet, tree.Id, out _));

        // No generated surface (no generator for the planet): the prop does not exist, and nothing throws.
        f.Terrain.GeneratedSurfaceRadius = null;
        var withoutSurface = new PropField(f.State, f.Terrain);

        Assert.False(withoutSurface.TryGetProp(Planet, tree.Id, out _));
        Assert.Empty(Collect(withoutSurface, Pole, 64f));
    }

    [Fact]
    public void TryGetProp_HeightOutsideEveryBand_False()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        Assert.True(new PropField(f.State, f.Terrain).TryGetProp(Planet, tree.Id, out _));

        // 1000 m over the radius: above the band of every kind (the widest ends at 300 m).
        f.Terrain.GeneratedSurfaceRadius = WorldFactory.HomePlanetParams.RadiusMeters + 1000f;

        Assert.False(new PropField(f.State, f.Terrain).TryGetProp(Planet, tree.Id, out _));
    }

    [Fact]
    public void TryGetProp_UnknownProfile_False()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        Assert.True(f.Field.TryGetProp(Planet, tree.Id, out _));

        // The same identifier in a world whose planet has a profile without a layout: no props at all.
        var other = new PropWorldFixture(state: WorldFactory.CreateWithProps(1, "core:test/1"));

        Assert.False(other.Field.TryGetProp(Planet, tree.Id, out _));
        Assert.Empty(Collect(other.Field, Pole, 64f));
    }

    [Fact]
    public void TryGetProp_UnknownPlanet_False()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        Assert.True(f.Field.TryGetProp(Planet, tree.Id, out _));

        Assert.False(f.Field.TryGetProp(new PlanetId(99), tree.Id, out _));
    }

    [Fact]
    public void CollectInSphere_ReturnsOnlyAnchorsInside()
    {
        const float radius = 30f;
        var f = new PropWorldFixture();
        var center = Pole + new Vector3(3f, 0f, -2f);
        var found = Collect(f.Field, center, radius);

        Assert.NotEmpty(found);
        Assert.Equal(found.Select(p => p.Id.Value).Order().ToArray(), found.Select(p => p.Id.Value).ToArray());
        Assert.Equal(found.Count, found.Select(p => p.Id).Distinct().Count());
        Assert.All(found, p => Assert.True(Vector3.Distance(p.Anchor, center) <= radius + 1e-3f, $"{p.Id} lies outside the sphere."));

        // Brute force over the cells around the sphere (a cell is 8 m wide, so 12 m more than the radius covers every offset) and both slots.
        var cells = new List<PropId>();
        CubeSphereGrid.CellsInSphere(center, radius + 12f, WorldFactory.HomePlanetParams.RadiusMeters, cells);
        var expected = new List<PropInstance>();
        foreach (var cell in cells)
        {
            foreach (var slot in new[] { 0, 1 })
            {
                var id = new PropId(cell.Value | ((ulong)slot << 16));
                if (f.Field.TryGetProp(Planet, id, out var instance) && Vector3.Distance(instance.Anchor, center) <= radius - 1e-3f)
                {
                    expected.Add(instance);
                }
            }
        }

        Assert.NotEmpty(expected);
        Assert.Subset(found.Select(p => p.Id).ToHashSet(), expected.Select(p => p.Id).ToHashSet());
    }

    [Fact]
    public void AnchorCache_SecondLookup_NoSurfaceCall()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        var field = new PropField(f.State, f.Terrain);
        var before = f.Terrain.GeneratedSurfaceCalls;

        Assert.True(field.TryGetProp(Planet, tree.Id, out var first));
        var afterFirst = f.Terrain.GeneratedSurfaceCalls;
        Assert.True(field.TryGetProp(Planet, tree.Id, out var second));

        Assert.True(afterFirst > before, "The first lookup must sample the generated surface.");
        Assert.Equal(afterFirst, f.Terrain.GeneratedSurfaceCalls);
        Assert.Equal(first, second);
    }
}

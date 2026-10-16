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

    [Fact]
    public void AnchorCache_Over4096Props_OldestSamplesAgain()
    {
        var f = new PropWorldFixture();
        var rules = new Horyzont.Core.Simulation.GameRules();
        Assert.Equal(4096, rules.PropAnchorCacheSize);

        // 4097 different props of a wide sphere, found by another field so that the field under test starts with an empty cache.
        var ids = Collect(f.Field, Pole, 400f).Select(p => p.Id).Take(rules.PropAnchorCacheSize + 1).ToArray();
        Assert.Equal(rules.PropAnchorCacheSize + 1, ids.Length);
        var field = new PropField(f.State, f.Terrain, rules);
        foreach (var id in ids)
        {
            Assert.True(field.TryGetProp(Planet, id, out _));
        }

        // The newest entry is still cached; the first one was the least recently used and was dropped, so it samples the surface again.
        var calls = f.Terrain.GeneratedSurfaceCalls;
        Assert.True(field.TryGetProp(Planet, ids[^1], out _));
        Assert.Equal(calls, f.Terrain.GeneratedSurfaceCalls);
        Assert.True(field.TryGetProp(Planet, ids[0], out _));
        Assert.True(f.Terrain.GeneratedSurfaceCalls > calls, "The oldest of 4097 props must have left the cache of 4096.");
    }

    [Fact]
    public void TryGetProp_AbsentAfterSampling_SecondLookupNoSurfaceCall()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);

        // The candidate exists, but the surface is 1000 m over the radius: above the band of every kind, so the prop is refused after sampling.
        f.Terrain.GeneratedSurfaceRadius = WorldFactory.HomePlanetParams.RadiusMeters + 1000f;
        var field = new PropField(f.State, f.Terrain);
        var before = f.Terrain.GeneratedSurfaceCalls;

        Assert.False(field.TryGetProp(Planet, tree.Id, out _));
        var afterFirst = f.Terrain.GeneratedSurfaceCalls;
        Assert.True(afterFirst > before, "The first lookup must sample the generated surface.");
        for (var i = 0; i < 100; i++)
        {
            Assert.False(field.TryGetProp(Planet, tree.Id, out _));
        }

        Assert.Equal(afterFirst, f.Terrain.GeneratedSurfaceCalls);
    }

    [Fact]
    public void TryGetProp_NoSurface_IsNotRemembered()
    {
        var f = new PropWorldFixture();
        var tree = f.FindProp(PropKindV1.Pine);
        var field = new PropField(f.State, f.Terrain);

        // No surface now: nothing was sampled, so the answer is not remembered and the prop exists as soon as the surface does.
        f.Terrain.GeneratedSurfaceRadius = null;
        Assert.False(field.TryGetProp(Planet, tree.Id, out _));
        f.Terrain.GeneratedSurfaceRadius = PropWorldFixture.Surface;

        Assert.True(field.TryGetProp(Planet, tree.Id, out _));
    }

    [Fact]
    public void CollectInSphere_SamplesOnlyCandidatesWhoseRayMeetsSphere()
    {
        const float radius = 2f;
        var f = new PropWorldFixture();
        var center = Pole + new Vector3(3f, 0f, -2f);
        Assert.True(f.State.Planets.TryGet(Planet, out var planet));
        Assert.True(PropLayouts.TryGet(planet.GeneratorProfile, out var layout));
        Assert.True(CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cellsPerFace));

        // Independent count in double precision: the candidates of the cells around the sphere, and those whose ray from the centre of the
        // planet passes within the radius of the centre of the sphere (only these can have an anchor inside it).
        var cells = new List<PropId>();
        CubeSphereGrid.CellsInSphere(center, radius + 30f, planet.RadiusMeters, cells);
        var candidates = 0;
        var meeting = 0;
        foreach (var cell in cells)
        {
            for (var slot = 0; slot < 4; slot++)
            {
                var id = new PropId(cell.Value | ((ulong)slot << 16));
                if (!layout!.TryGetCandidate(planet.Seed, id, out var candidate))
                {
                    continue;
                }

                candidates++;
                var d = CubeSphereGrid.DirectionOfPoint(id.Face, id.CellU + (candidate.OffsetU / 1024.0), id.CellV + (candidate.OffsetV / 1024.0), cellsPerFace);
                var along = Vector3.Dot(center, d);
                if ((center - (d * along)).Length() <= radius + 0.02f)
                {
                    meeting++;
                }
            }
        }

        var field = new PropField(f.State, f.Terrain);
        var before = f.Terrain.GeneratedSurfaceCalls;
        var found = Collect(field, center, radius);
        var sampled = f.Terrain.GeneratedSurfaceCalls - before;

        Assert.True(candidates > meeting + 5, $"The scene must have candidates that the filter can skip ({candidates} candidates, {meeting} meet the sphere).");
        Assert.True(sampled <= meeting, $"{sampled} surface samples for {meeting} candidates whose ray meets the sphere.");
        Assert.True(found.Count <= meeting);
    }
}

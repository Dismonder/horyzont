using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Survival;
using Horyzont.Core.World.Constructs;
using Horyzont.Core.World.Terrain;
using Horyzont.Testing;

using static Horyzont.Core.Tests.Survival.ShelterScenes;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="ShelterEvaluator"/> (K6-34 D11): 26 rays of 16 m from the origin; a ray hits a block of a construct of the planet or the terrain; a shelter needs the ray straight up and at least 21 hits. Every scene
/// checks the number of hits and the ray up, not only the verdict, so a scene cannot pass for another reason than the one it is named for (review B1, P2-3). The scenes are those of
/// <see cref="ShelterScenes"/>: in the open field the nine rays pointing down hit the terrain and nothing else does. Every test of a refusal first evaluates the twin that shelters.
/// </summary>
public sealed class ShelterEvaluatorTests
{
    private static readonly PlanetId _home = WorldFactory.HomePlanet;

    private static ShelterResult Evaluate(SimFixture f, Vector3 origin, HazardRules? rules = null, PlanetId? planet = null) =>
        ShelterEvaluator.Evaluate(f.State, f.Terrain, planet ?? _home, origin, rules ?? HazardRules.Default);

    private static void AssertSheltered(ShelterResult result, int hits)
    {
        Assert.Equal(hits, result.Hits);
        Assert.True(result.UpHit);
        Assert.True(result.Sheltered);
    }

    [Fact]
    public void OpenField_NotSheltered()
    {
        // The twin: the closed hut around the same point shelters.
        AssertSheltered(Evaluate(Scene(Closed), Center), 26);

        var open = Evaluate(new SimFixture(), Center);

        Assert.Equal(9, open.Hits);
        Assert.False(open.UpHit);
        Assert.False(open.Sheltered);
    }

    [Fact]
    public void ClosedHut_Sheltered()
    {
        var result = Evaluate(Scene(Closed), Center);

        AssertSheltered(result, 26);
        Assert.Equal(ShelterDirections.Count, result.Hits);
    }

    [Fact]
    public void HutWithOpenDoorway_Sheltered()
    {
        var result = Evaluate(Scene(WithDoorway), Center);

        // One ray (along +X, through the missing cell) escapes: the hut with an opening is still a shelter, and the 25 hits are what proves that the opening exists.
        AssertSheltered(result, 25);
        Assert.InRange(result.Hits, 21, 25);
    }

    [Fact]
    public void RoofWithoutWalls_NotSheltered()
    {
        AssertSheltered(Evaluate(Scene(Closed), Center), 26);

        var result = Evaluate(Scene(RoofOnly), Center);

        // The nine rays up hit the roof, the nine down the terrain, the eight horizontal ones nothing: the ray up is there, the hits are not.
        Assert.True(result.UpHit);
        Assert.Equal(18, result.Hits);
        Assert.True(result.Hits < 21);
        Assert.False(result.Sheltered);
    }

    [Fact]
    public void WallsWithoutRoof_NotSheltered()
    {
        AssertSheltered(Evaluate(Scene(Closed), Center), 26);

        var result = Evaluate(Scene(TallWallsNoRoof, height: 13), Center);

        // Eight horizontal rays and the eight up-diagonal ones hit the high walls, the nine down the terrain: enough hits, but the ray straight up is free.
        Assert.False(result.UpHit);
        Assert.Equal(25, result.Hits);
        Assert.True(result.Hits >= 21);
        Assert.False(result.Sheltered);
    }

    [Fact]
    public void RequiredHits_Boundary()
    {
        var f = Scene(WithDoorway);
        var n = Evaluate(f, Center).Hits;
        Assert.Equal(25, n);

        // The limit is inclusive: with exactly the hits of the scene it shelters, with one more required it does not. (Catches >= turned into >.)
        Assert.True(Evaluate(f, Center, HazardRules.Default with { ShelterRequiredHits = n }).Sheltered);
        Assert.False(Evaluate(f, Center, HazardRules.Default with { ShelterRequiredHits = n + 1 }).Sheltered);

        // The number of hits does not depend on the rule.
        Assert.Equal(n, Evaluate(f, Center, HazardRules.Default with { ShelterRequiredHits = n + 1 }).Hits);
    }

    [Fact]
    public void RayUp_IsRequiredBesidesTheHits()
    {
        // A hut with a skylight has 25 hits; with the rule at 1 hit the verdict is still decided by the ray up. (Catches a verdict without UpHit.)
        var f = Scene(WithSkylight);
        var rules = HazardRules.Default with { ShelterRequiredHits = 1 };

        var result = Evaluate(f, Center, rules);

        Assert.Equal(25, result.Hits);
        Assert.False(result.UpHit);
        Assert.False(result.Sheltered);
        Assert.True(Evaluate(Scene(Closed), Center, rules).Sheltered);
    }

    [Fact]
    public void Cave_Sheltered()
    {
        var f = new SimFixture();
        f.Terrain.Cavities.Add(new TerrainCavity(new Vector3(0f, 3980f, 0f), 8f));

        // Inside the terrain the rays leave the cave through its wall below the surface: all 26 hit.
        AssertSheltered(Evaluate(f, new Vector3(0f, 3980f, 0f)), 26);
    }

    [Fact]
    public void CaveOpenToSky_NotSheltered()
    {
        var closed = new SimFixture();
        closed.Terrain.Cavities.Add(new TerrainCavity(new Vector3(0f, 3980f, 0f), 8f));
        AssertSheltered(Evaluate(closed, new Vector3(0f, 3980f, 0f)), 26);

        // The same cave 16 m higher reaches above the surface (4000 m): the rays that leave it through the opening, the nine pointing up, hit nothing.
        var open = new SimFixture();
        open.Terrain.Cavities.Add(new TerrainCavity(new Vector3(0f, 3996f, 0f), 8f));

        var result = Evaluate(open, new Vector3(0f, 3996f, 0f));

        Assert.False(result.UpHit);
        Assert.Equal(17, result.Hits);
        Assert.False(result.Sheltered);
    }

    [Fact]
    public void ShipParkedOnPlanet_InteriorSheltered()
    {
        AssertSheltered(Evaluate(Scene(Closed, ship: true), Center), 26);
        Assert.Equal(ConstructMode.Ship, Scene(Closed, ship: true).State.Constructs.All.Single().Mode);
    }

    [Fact]
    public void OtherPlanetConstruct_Ignored()
    {
        var f = Scene(Closed, secondPlanet: true);

        // The control: on the second planet, at the same coordinates, the hut shelters; on the home planet it is not there.
        AssertSheltered(Evaluate(f, Center, planet: WorldFactory.SecondPlanet), 26);
        var home = Evaluate(f, Center, planet: _home);
        Assert.Equal(9, home.Hits);
        Assert.False(home.Sheltered);
    }

    [Theory]
    [InlineData(0f, false)]
    [InlineData(30f, true)]
    [InlineData(90f, true)]
    public void HutOnTiltedConstruct_UsesPlanetUp(float degrees, bool sheltered)
    {
        // The hut has a skylight in the roof. Untilted, the ray straight up (the up of the planet) passes through it. Turned about the Z axis, the skylight faces sideways and the up of the planet
        // meets a wall: the verdict follows the up of the planet, never the axes of the construct. (A ray along the Y axis of the construct would escape in every one of these cases.)
        var rotation = AboutZ(degrees);
        var f = Scene(WithSkylight, rotation);

        var result = Evaluate(f, CenterOf(rotation));

        Assert.Equal(sheltered, result.UpHit);
        Assert.Equal(sheltered, result.Sheltered);
        if (degrees is 0f or 90f)
        {
            Assert.Equal(25, result.Hits);
        }
        else
        {
            Assert.True(result.Hits >= 24);
        }
    }

    [Fact]
    public void ShipLyingOnItsSide_InteriorSheltered()
    {
        // A closed ship turned on its side (90 degrees) is as closed as one standing: all 26 rays hit.
        var rotation = AboutZ(90f);
        var f = Scene(Closed, rotation, ship: true);

        AssertSheltered(Evaluate(f, CenterOf(rotation)), 26);

        // The skylight in the roof of the standing ship becomes a window in the wall of the one on its side: with the ship on its side the ray up meets the hull.
        Assert.False(Evaluate(Scene(WithSkylight), Center).UpHit);
        Assert.True(Evaluate(Scene(WithSkylight, rotation, ship: true), CenterOf(rotation)).UpHit);
    }

    [Fact]
    public void PlayerInConstructFrame_OriginInPlanetFrame()
    {
        var rotation = AboutZ(30f);
        var f = Scene(Closed, rotation, ship: true);
        var player = f.Join();
        var state = f.PlayerOf(player);
        var local = new Vector3(2.5f, 1f, 2.5f);

        f.Sim.ApplyServerOperation(new EntityReparentOp(state.Entity, FrameRef.Of(HutId), state.ParentSeq + 1, local, Quaternion.Identity, Vector3.Zero, null));

        // The position of the player is local to the construct; the origin of the rays is the feet in the frame of the planet plus the up of the planet times the height of D11.
        Assert.True(ShelterEvaluator.TryGetOrigin(f.State, state, HazardRules.Default, out var planet, out var origin));
        var feet = Corner + Vector3.Transform(local, rotation);
        var expected = feet + (TerrainMath.UpAt(feet) * HazardRules.Default.ShelterOriginHeightMeters);
        Assert.Equal(_home, planet);
        Assert.Equal(expected.X, origin.X, 3);
        Assert.Equal(expected.Y, origin.Y, 3);
        Assert.Equal(expected.Z, origin.Z, 3);
        Assert.True(Vector3.Distance(origin, local) > 1000f);

        // The origin is inside the interior of the hut: the evaluation shelters.
        AssertSheltered(Evaluate(f, origin), 26);
    }
}

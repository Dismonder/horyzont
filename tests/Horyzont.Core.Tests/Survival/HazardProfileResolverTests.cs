using Horyzont.Core.Content;
using Horyzont.Core.Ids;
using Horyzont.Core.Survival;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// <see cref="HazardProfileResolver.Resolve"/> (K6-34 D9): the profile of the body the planet is in the star system (the home planet is body 0 of a world without one), else the default profile; a point
/// without a planet or above the atmosphere gets the profile of space. The world is <see cref="WorldFactory.CreateWithStarSystem"/> (body 0 the home planet, bodies 1 to 3 the other planets of
/// <see cref="Horyzont.Core.Space.StarSystemLayout.V1"/>); the content is <see cref="HazardTestContent.Hazards"/> (bodies 0 to 3 have profiles, the default is the temperate one).
/// </summary>
public sealed class HazardProfileResolverTests
{
    private static readonly HazardContent _hazards = HazardTestContent.Hazards;

    private static WorldState StarWorld() => WorldFactory.CreateWithStarSystem(77);

    private static PlanetId BodyPlanet(WorldState state, int body) => state.StarSystem!.Bodies.Single(b => b.BodyIndex == body).Planet;

    private static HazardProfileDef Resolve(WorldState state, PlanetId? planet, float height = 0f, HazardContent? hazards = null) =>
        HazardProfileResolver.Resolve(state, hazards ?? _hazards, planet, height);

    [Fact]
    public void Body0_Home()
    {
        var state = StarWorld();

        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, state.HomePlanet).Key);
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, BodyPlanet(state, 0)).Key);
    }

    [Fact]
    public void Body1_Moon()
    {
        var state = StarWorld();

        // The twin: body 0 is the temperate profile, body 1 differs (the moon has vacuum).
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, BodyPlanet(state, 0)).Key);
        Assert.Equal(HazardTestContent.MoonKey, Resolve(state, BodyPlanet(state, 1)).Key);
        Assert.Equal(HazardTestContent.ToxicKey, Resolve(state, BodyPlanet(state, 2)).Key);
        Assert.Equal(HazardTestContent.FrozenKey, Resolve(state, BodyPlanet(state, 3)).Key);
    }

    [Fact]
    public void NoStarSystem_HomeIsBody0()
    {
        // A world without a star system (an old world before the backfill, or the tests of the other packages): the home planet is body 0, a second planet has no body.
        var state = WorldFactory.CreateWith(WorldFactory.HomePlanetParams, WorldFactory.SecondPlanetParams);
        Assert.Null(state.StarSystem);
        var remapped = _hazards with { Bodies = [new HazardBodyDef(0, HazardTestContent.ToxicKey)], DefaultProfile = HazardTestContent.MoonKey };

        // The mapping of body 0 is read (not the default), and the second planet falls back to the default.
        Assert.Equal(HazardTestContent.ToxicKey, Resolve(state, WorldFactory.HomePlanet, hazards: remapped).Key);
        Assert.Equal(HazardTestContent.MoonKey, Resolve(state, WorldFactory.SecondPlanet, hazards: remapped).Key);
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, WorldFactory.HomePlanet).Key);
    }

    [Fact]
    public void UnknownBody_Default()
    {
        var state = StarWorld();
        var fewer = _hazards with { Bodies = [new HazardBodyDef(0, HazardTestContent.TemperateKey)], DefaultProfile = HazardTestContent.FrozenKey };

        // Body 0 has an entry, bodies 1 to 3 have none: they get the default profile, which here differs from the profile of body 0.
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, BodyPlanet(state, 0), hazards: fewer).Key);
        Assert.Equal(HazardTestContent.FrozenKey, Resolve(state, BodyPlanet(state, 1), hazards: fewer).Key);
        Assert.Equal(HazardTestContent.FrozenKey, Resolve(state, BodyPlanet(state, 3), hazards: fewer).Key);

        // A planet the world does not know is a body of no entry too.
        Assert.Equal(HazardTestContent.FrozenKey, Resolve(state, new PlanetId(999_999), hazards: fewer).Key);
    }

    [Fact]
    public void AboveAtmosphere_Space()
    {
        var state = StarWorld();
        var planet = BodyPlanet(state, 1);
        Assert.True(state.Planets.TryGet(planet, out var params1));
        var atmosphere = params1.AtmosphereHeightMeters;
        Assert.True(atmosphere > 0f);

        // The atmosphere is inclusive: at its top the body's profile applies, a metre above it is space.
        Assert.Equal(HazardTestContent.MoonKey, Resolve(state, planet, atmosphere).Key);
        Assert.Equal(HazardTestContent.SpaceKey, Resolve(state, planet, atmosphere + 1f).Key);
        Assert.Equal(HazardTestContent.MoonKey, Resolve(state, planet, 0f).Key);
        Assert.Equal(HazardTestContent.MoonKey, Resolve(state, planet, -50f).Key);
    }

    [Fact]
    public void OffPlanet_Space()
    {
        var state = StarWorld();
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, state.HomePlanet).Key);

        // A player in the frame of the star system (a ship in flight) is on no planet.
        Assert.Equal(HazardTestContent.SpaceKey, Resolve(state, null).Key);
        Assert.Equal(HazardTestContent.SpaceKey, Resolve(state, null, 100_000f).Key);
    }

    [Fact]
    public void NonFiniteHeight_DoesNotThrow()
    {
        var state = StarWorld();
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, state.HomePlanet).Key);

        // A height that is not a number is not above the atmosphere (the rates are zero for it anyway, HazardField); the resolver answers and does not throw.
        Assert.Equal(HazardTestContent.TemperateKey, Resolve(state, state.HomePlanet, float.NaN).Key);
    }
}

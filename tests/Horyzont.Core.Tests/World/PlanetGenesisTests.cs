using FsCheck.Xunit;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.World;

public sealed class PlanetGenesisTests
{
    public static TheoryData<ulong, uint, ulong, ulong> Golden => new()
    {
        { 0x484F52595A4F4E54UL, 0u, 18194220777529056261UL, 2017951753422106138UL },
        { 0UL, 0u, 3326852307356962982UL, 14878437703437867665UL },
        { ulong.MaxValue, 7u, 18006734346423165329UL, 16455535189017469255UL },
    };

    [Theory]
    [MemberData(nameof(Golden))]
    public void DerivePlanetId_GoldenValues(ulong galaxySeed, uint index, ulong expectedId, ulong expectedSeed)
    {
        Assert.Equal(expectedId, PlanetGenesis.DerivePlanetId(galaxySeed, index).Value);
        Assert.Equal(expectedSeed, PlanetGenesis.DerivePlanetSeed(galaxySeed, index));
    }

    [Property]
    public bool DerivePlanetId_NeverZero(ulong galaxySeed, uint index) =>
        PlanetGenesis.DerivePlanetId(galaxySeed, index).Value != 0;

    [Fact]
    public void DerivePlanetSeed_DiffersFromPlanetId()
    {
        foreach (var (seed, index) in new[] { (0UL, 0u), (1UL, 1u), (0x484F52595A4F4E54UL, 0u), (ulong.MaxValue, uint.MaxValue) })
        {
            Assert.NotEqual(PlanetGenesis.DerivePlanetId(seed, index).Value, PlanetGenesis.DerivePlanetSeed(seed, index));
        }
    }

    [Fact]
    public void CreatePlanet_DefaultsMatchSpec()
    {
        var planet = PlanetGenesis.CreatePlanet(42, PlanetGenesis.HomePlanetIndex, "core:planet/1");

        Assert.Equal(new SystemId(1), planet.System);
        Assert.Equal("core:planet/1", planet.GeneratorProfile);
        Assert.Equal(4000f, planet.RadiusMeters);
        Assert.Equal(1f, planet.VoxelSizeMeters);
        Assert.Equal(9.81f, planet.Gravity);
        Assert.Equal(1000f, planet.AtmosphereHeightMeters);
        Assert.Equal(400f, planet.CoreDepthMeters);
        Assert.Equal(1800f, planet.DayLengthSeconds);
        Assert.Equal(PlanetGenesis.DerivePlanetId(42, 0), planet.Id);
        Assert.Equal(PlanetGenesis.DerivePlanetSeed(42, 0), planet.Seed);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void CreatePlanet_EmptyProfile_Throws(string? profile) =>
        Assert.Throws<ArgumentException>(() => PlanetGenesis.CreatePlanet(42, 0, profile!));

    [Fact]
    public void CreatePlanet_ResultPassesWorldCreation()
    {
        var planet = PlanetGenesis.CreatePlanet(42, 0, "core:planet/1");

        var state = WorldState.CreateNew(WorldFactory.DefaultWorldId, 42, [planet], [WorldFactory.StoneBlockKey], ["stone"]);

        Assert.Equal(planet, Assert.Single(state.Planets.All));
    }
}

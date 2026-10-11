using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Space;

public sealed class StarSystemGenesisTests
{
    private const string Profile = "core:planet/1";

    [Fact]
    public void StarSystemGenesis_SameSeed_BitwiseIdentical()
    {
        foreach (var seed in new ulong[] { 0, 1, 0xDEADBEEF, ulong.MaxValue })
        {
            var a = StarSystemGenesis.CreateWorldBodies(seed, Profile);
            var b = StarSystemGenesis.CreateWorldBodies(seed, Profile);

            Assert.Equal(Describe(a), Describe(b));
            Assert.Equal(a.System, b.System);
        }
    }

    [Fact]
    public void StarSystemGenesis_GoldenSeeds_MatchFrozenValues()
    {
        // Frozen values of StarSystemLayout.V1 (decision 3): any change here means a new layout version, never an edit.
        Assert.Equal(GoldenSeed0, Describe(StarSystemGenesis.CreateWorldBodies(0, Profile)));
        Assert.Equal(GoldenSeed1, Describe(StarSystemGenesis.CreateWorldBodies(1, Profile)));
        Assert.Equal(GoldenSeedDeadBeef, Describe(StarSystemGenesis.CreateWorldBodies(0xDEADBEEF, Profile)));
    }

    [Fact]
    public void StarSystemGenesis_ZonesSeparated_Across10000Seeds()
    {
        var worst = double.MaxValue;
        for (ulong i = 0; i < 10_000; i++)
        {
            var seed = i * 0x9E3779B97F4A7C15UL;
            var created = StarSystemGenesis.CreateWorldBodies(seed, Profile);
            var bodies = created.System.Bodies;
            for (var a = 0; a < bodies.Count; a++)
            {
                for (var b = a + 1; b < bodies.Count; b++)
                {
                    var distance = Vector3D.Distance(bodies[a].Pose.Position, bodies[b].Pose.Position);
                    var required = (StarSystemRules.ZoneExitFactor * (Radius(created, a) + Radius(created, b))) + StarSystemRules.MinZoneGapMeters;
                    Assert.True(distance >= required, $"seed {seed}: bodies {a} and {b} are {distance} m apart, need {required} m");
                    worst = Math.Min(worst, distance - required);
                }
            }
        }

        Assert.True(worst > 0, $"smallest margin {worst} m");
    }

    [Fact]
    public void StarSystemLayout_V1_WorstCaseSeparated()
    {
        var layout = StarSystemLayout.V1;
        var home = PlanetGenesis.CreatePlanet(7, PlanetGenesis.HomePlanetIndex, Profile);
        var count = layout.Bodies.Count;
        Assert.Equal(3, count);

        // Distances of bodies 1..3 from the home planet after the construction rule, at both extremes of their range.
        var minDistance = new double[count];
        var maxDistance = new double[count];
        for (var k = 0; k < count; k++)
        {
            var body = layout.Bodies[k];
            var lift = StarSystemGenesis.MinimumDistanceFromHome(home.RadiusMeters, body.MaxRadiusMeters);
            minDistance[k] = Math.Max(body.MinDistanceMeters, StarSystemGenesis.MinimumDistanceFromHome(home.RadiusMeters, body.MinRadiusMeters));
            maxDistance[k] = Math.Max(body.MaxDistanceMeters, lift);
            Assert.True(minDistance[k] - 1 >= (StarSystemRules.ZoneExitFactor * (home.RadiusMeters + body.MaxRadiusMeters)) + StarSystemRules.MinZoneGapMeters
                || body.MinDistanceMeters < lift);
        }

        // Pairs among bodies 1..3: the worst case is both on the same ray, the inner one as far and the outer one as near as possible.
        for (var a = 0; a < count; a++)
        {
            for (var b = a + 1; b < count; b++)
            {
                var gap = minDistance[b] - maxDistance[a] - 2; // two positions rounded to whole metres
                var required = (StarSystemRules.ZoneExitFactor * (layout.Bodies[a].MaxRadiusMeters + layout.Bodies[b].MaxRadiusMeters))
                    + StarSystemRules.MinZoneGapMeters;
                Assert.True(gap >= required, $"bodies {a + 1} and {b + 1}: worst gap {gap} m, need {required} m");
            }
        }
    }

    [Fact]
    public void StarSystemGenesis_V1_BodiesWithinSpecRanges()
    {
        for (ulong seed = 0; seed < 2_000; seed++)
        {
            var created = StarSystemGenesis.CreateWorldBodies(seed * 31 + 5, Profile);
            Assert.Equal(4, created.Bodies.Count);
            Assert.Equal(4, created.System.Bodies.Count);
            AssertBody(created, 1, 1400, 1600, 1.62f, 1.62f, 600, 150, 17_500, 18_500);
            AssertBody(created, 2, 3000, 3500, 7.50f, 9.00f, 1000, 300, 48_000, 52_000);
            AssertBody(created, 3, 4500, 5000, 10.50f, 12.00f, 1500, 500, 108_000, 110_000);
            foreach (var planet in created.Bodies)
            {
                Assert.True(planet.AtmosphereHeightMeters > 300, "every body needs an atmosphere above the maximum relief");
            }
        }
    }

    [Fact]
    public void StarSystemGenesis_HomeIsGivenPlanetAtOrigin()
    {
        var home = PlanetGenesis.CreatePlanet(42, PlanetGenesis.HomePlanetIndex, Profile) with { DayLengthSeconds = 1200 };
        var created = StarSystemGenesis.Create(42, home, StarSystemLayout.V1);

        Assert.Same(home, created.Bodies[0]);
        Assert.Equal(home.Id, created.System.HomePlanet);
        Assert.Equal(new SystemId(1), created.System.System);
        Assert.Equal(1, created.System.LayoutVersion);
        Assert.Equal(1200f, created.System.SunCycleSeconds);
        Assert.Equal(home.Id, created.System.Bodies[0].Planet);
        Assert.Equal(0, created.System.Bodies[0].BodyIndex);
        Assert.Equal(PoseD.Identity, created.System.Bodies[0].Pose);
        for (var k = 1; k < created.Bodies.Count; k++)
        {
            Assert.Equal(QuaternionD.Identity, created.System.Bodies[k].Pose.Rotation);
            Assert.Equal(home.GeneratorProfile, created.Bodies[k].GeneratorProfile);
            Assert.Equal(1200f, created.Bodies[k].DayLengthSeconds);
            Assert.Equal(new SystemId(1), created.Bodies[k].System);
        }
    }

    [Fact]
    public void StarSystemGenesis_BodyIdsFromPlanetGenesis()
    {
        const ulong seed = 0xC0FFEE;
        var created = StarSystemGenesis.CreateWorldBodies(seed, Profile);

        Assert.Equal(PlanetGenesis.CreatePlanet(seed, PlanetGenesis.HomePlanetIndex, Profile), created.Bodies[0]);
        for (uint k = 1; k < 4; k++)
        {
            Assert.Equal(PlanetGenesis.DerivePlanetId(seed, k), created.Bodies[(int)k].Id);
            Assert.Equal(PlanetGenesis.DerivePlanetSeed(seed, k), created.Bodies[(int)k].Seed);
            Assert.Equal(created.Bodies[(int)k].Id, created.System.Bodies[(int)k].Planet);
            Assert.Equal(k, created.System.Bodies[(int)k].BodyIndex);
        }
    }

    private static double Radius(GeneratedStarSystem created, int index) => created.Bodies[index].RadiusMeters;

    private static void AssertBody(
        GeneratedStarSystem created, int k, float minR, float maxR, float minG, float maxG, float atmosphere, float core, double minD, double maxD)
    {
        var planet = created.Bodies[k];
        var home = created.Bodies[0];
        Assert.InRange(planet.RadiusMeters, minR, maxR);
        Assert.Equal(0, planet.RadiusMeters % 10);
        Assert.InRange(planet.Gravity, minG - 1e-6f, maxG + 1e-6f);
        Assert.Equal(Math.Round(planet.Gravity * 100f), planet.Gravity * 100f, 3);
        Assert.Equal(atmosphere, planet.AtmosphereHeightMeters);
        Assert.Equal(core, planet.CoreDepthMeters);

        var position = created.System.Bodies[k].Pose.Position;
        Assert.Equal(Math.Round(position.X), position.X);
        Assert.Equal(Math.Round(position.Y), position.Y);
        Assert.Equal(Math.Round(position.Z), position.Z);
        var distance = position.Length();
        var lowest = Math.Max(minD, StarSystemGenesis.MinimumDistanceFromHome(home.RadiusMeters, planet.RadiusMeters));
        Assert.InRange(distance, lowest - 1, Math.Max(maxD, lowest) + 1);
        Assert.True(Math.Abs(position.Y) <= (distance * Math.Sin(5 * Math.PI / 180)) + 1, "elevation within 5 degrees");
    }

    private static string Describe(GeneratedStarSystem created)
    {
        var parts = new List<string>();
        for (var k = 0; k < created.Bodies.Count; k++)
        {
            var p = created.Bodies[k];
            var pose = created.System.Bodies[k].Pose;
            parts.Add(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{k}:{p.Id.Value:X16} R={p.RadiusMeters} g={BitConverter.SingleToInt32Bits(p.Gravity):X8} at={p.AtmosphereHeightMeters} core={p.CoreDepthMeters} pos={pose.Position.X},{pose.Position.Y},{pose.Position.Z}"));
        }

        return string.Join(" | ", parts);
    }

    // Filled after the first green run (plan step 1); approved in the PR.
    private const string GoldenSeed0 = "";
    private const string GoldenSeed1 = "";
    private const string GoldenSeedDeadBeef = "";
}

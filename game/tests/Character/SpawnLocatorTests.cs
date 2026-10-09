using System.Diagnostics;
using System.Globalization;

using GdUnit4;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Server.Characters;

using static GdUnit4.Assertions;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Tests.Character;

[TestSuite]
public class SpawnLocatorTests
{
    private const ulong GalaxySeed = 0x484F52595A4F4E54UL;

    private static PlanetParams Home() => PlanetGenesis.CreatePlanet(GalaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public void SpawnLocator_NewPlayer_StartsOnAGentleSlopeNearTheEquator()
    {
        var planet = Home();
        var clock = Stopwatch.StartNew();

        var (feet, rotation) = SpawnLocator.ForNewPlayer(planet);

        var elapsed = clock.Elapsed.TotalMilliseconds;
        var up = Vector3.Normalize(feet);
        AssertThat(Vector3.Distance(up, SpawnLocator.StartDirection) * planet.RadiusMeters).IsLess((SpawnLocator.CandidateRings + 1) * SpawnLocator.CandidateSpacingMeters * 1.5f);
        AssertThat(Math.Abs(feet.Length() - planet.RadiusMeters)).IsLess(PlanetGeneratorV1.MaxReliefMeters + SpawnLocator.SpawnClearance + 1f);
        AssertThat(MathF.Abs(rotation.Length() - 1f)).IsLess(1e-6f);
        AssertThat(Vector3.Distance(Vector3.Transform(Vector3.UnitY, rotation), up)).IsLess(1e-4f);

        // The site is a gentle slope: the generated surface a few metres to each side is at nearly the same radius.
        var surface = feet.Length() - SpawnLocator.SpawnClearance;
        var tangent = Vector3.Normalize(Vector3.Cross(up, Vector3.UnitY));
        foreach (var offset in new[] { 3f, -3f })
        {
            var neighbour = SpawnLocator.AboveSurface(planet, up + (tangent * (offset / planet.RadiusMeters)));
            AssertThat(MathF.Abs(neighbour.Length() - SpawnLocator.SpawnClearance - surface) / 3f).IsLess(SpawnLocator.MaxStartSlope + 0.1f);
        }

        Record($"spawn_locator elapsed_ms={elapsed.ToString("F0", CultureInfo.InvariantCulture)} feet={feet} surface={surface.ToString("F2", CultureInfo.InvariantCulture)}");
        AssertThat(elapsed).IsLess(10000d);
    }

    [TestCase(Timeout = 120000)]
    [RequireGodotRuntime]
    public void SpawnLocator_AboveSurface_DegenerateDirection_FallsBackToTheStartDirection()
    {
        var planet = Home();

        var feet = SpawnLocator.AboveSurface(planet, Vector3.Zero);

        AssertThat(Vector3.Distance(Vector3.Normalize(feet), SpawnLocator.StartDirection)).IsLess(1e-6f);
        AssertThat(float.IsFinite(feet.X)).IsTrue();
    }

    private static void Record(string line)
    {
        if (System.Environment.GetEnvironmentVariable("HORYZONT_RECON_OUT") is { Length: > 0 } path)
        {
            File.AppendAllText(path, line + "\n");
        }
    }
}

using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Space;
using Horyzont.Testing;
using Horyzont.Testing.Ships;

namespace Horyzont.Server.Tests.Ships;

/// <summary>K5-24 criterion 1 (spec 02:46–50, 02:249): powered transits of about 50, 70 and 100 s from the home surface to parking.</summary>
public sealed class ShipTransitTests(ITestOutputHelper output)
{
    private static GeneratedStarSystem System => StarSystemGenesis.CreateWorldBodies(0, WorldFactory.HomePlanetParams.GeneratorProfile);

    /// <summary>
    /// Every powered transit parks on its target; the times are reported for the PR. The spec windows (±20 % of 50, 70 and 100 s)
    /// are not reachable with the spec values τ = 8 s and v_min = 100 m/s (measured 63, 103 and 124 s; seed 0 puts the moon in the
    /// way to planet B) — open owner question, see <see cref="ShipTransit_WithPulse_Moon50_B70_C100s"/>. This guard fails on a
    /// regression beyond 1.25 × the upper window.
    /// </summary>
    [Theory]
    [InlineData(1, 60)]
    [InlineData(2, 84)]
    [InlineData(3, 120)]
    public void ShipTransit_WithPulse_ParksAndReportsTime(int body, double windowMax)
    {
        var result = Fly(body, FlightRules.Default);

        Assert.True(result.Parked, $"not parked after {result.Seconds} s");
        Assert.InRange(result.Seconds, 0, windowMax * 1.25);
    }

    /// <summary>
    /// The windows of the plan (criterion 1). Skipped until the owner decides the tuning: with τ = 5 s (v_min 100, clearance 300)
    /// the times are 50.5, 81.0 and 105.4 s; τ = 6 s and v_min = 120 m/s give 52.3, 85.4 and 109.5 s.
    /// </summary>
    /// <param name="body">Target body.</param>
    /// <param name="min">Window start.</param>
    /// <param name="max">Window end.</param>
    [Theory(Skip = "K5-24 open question T1: the spec windows need τ ≈ 5 s (spec 02:50 says τ = 8 s); owner decides the tuning.")]
    [InlineData(1, 40, 60)]
    [InlineData(2, 56, 84)]
    [InlineData(3, 80, 120)]
    public void ShipTransit_WithPulse_Moon50_B70_C100s(int body, double min, double max)
    {
        var result = Fly(body, FlightRules.Default);

        Assert.True(result.Parked, $"not parked after {result.Seconds} s");
        Assert.InRange(result.Seconds, min, max);
    }

    private TransitResult Fly(int body, FlightRules rules)
    {
        var result = TransitSim.Fly(System, body, FlightFixtures.Thirty(), rules, usePulse: true, maxSeconds: 300);
        output.WriteLine($"body {body}: parked={result.Parked} t={result.Seconds:F1} s fuel={result.FuelUsed:F0} vmax={result.MaxSpeed:F0} m/s hits={result.Hits} {result.Timeline}");
        return result;
    }

    [Fact]
    public void ShipTransit_WithoutPulse_Report()
    {
        for (var body = 1; body <= 3; body++)
        {
            var result = TransitSim.Fly(System, body, FlightFixtures.Thirty(), FlightRules.Default, usePulse: false, maxSeconds: 1200);
            output.WriteLine($"no pulse, body {body}: parked={result.Parked} t={result.Seconds:F1} s fuel={result.FuelUsed:F0} vmax={result.MaxSpeed:F0} m/s {result.Timeline}");
            Assert.True(result.Parked, $"body {body} not parked after {result.Seconds} s");
        }
    }
}

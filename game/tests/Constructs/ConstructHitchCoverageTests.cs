using Horyzont.Game.Testing;

namespace Horyzont.Game.Tests.Constructs;

public sealed class ConstructHitchCoverageTests : EngineTestBase
{
    [EngineTest]
    public async Task RemeshStats_MissingCallbacksOrUndrainedWork_RejectsMeasurement()
    {
        await NextFrame();
        var rejected = 0;
        foreach (var scenario in new[]
        {
            (Jobs: 0, Pending: 0, Expected: 0, Samples: Array.Empty<double>()),
            (Jobs: 0, Pending: 1, Expected: 2, Samples: new[] { 10.0 }),
            (Jobs: 1, Pending: 0, Expected: 1, Samples: new[] { 10.0 }),
            (Jobs: 0, Pending: 0, Expected: 2, Samples: new[] { 10.0 }),
        })
        {
            try
            {
                ConstructHitchScenarios.RemeshStats(scenario.Jobs, scenario.Pending, scenario.Expected, scenario.Samples);
            }
            catch (EngineTestFailedException)
            {
                // These invalid measurements must fail the acceptance gate.
                rejected++;
            }
        }

        EngineAssert.Equal(4, rejected, "invalid measurements rejected");
        var samples = new List<double> { 10.0, 20.0 };
        var valid = ConstructHitchScenarios.RemeshStats(0, 0, 2, samples);
        EngineAssert.Equal(2, valid.Count, "complete callback coverage produces latency samples");
    }
}

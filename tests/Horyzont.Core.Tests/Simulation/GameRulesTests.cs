using Horyzont.Core.Simulation;

namespace Horyzont.Core.Tests.Simulation;

public sealed class GameRulesTests
{
    [Fact]
    public void MaxToolRadius_NotAboveApplierRadiusLimit()
    {
        Assert.True(new GameRules().MaxToolRadius <= TerrainRules.MaxEditRadiusMeters);
    }

    [Fact]
    public void ItemIntentBucket_TwentyPerSecondBurstForty()
    {
        var rules = new GameRules();

        Assert.Equal(20, rules.ItemIntentsPerSecond);
        Assert.Equal(40, rules.ItemIntentBurst);
    }
}

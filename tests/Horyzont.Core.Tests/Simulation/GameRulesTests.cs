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

    [Fact]
    public void CraftingRules_DefaultsOfK6_32()
    {
        var rules = new GameRules();

        // K6-32 D12 to D16, D26 and the limit of review A1 note a (a construct with the most stations fits one reliable message).
        Assert.Equal(50, rules.MaxCraftCount);
        Assert.Equal(20f, rules.CraftContainerRangeMeters);
        Assert.Equal(8f, rules.StationUseRangeMeters);
        Assert.Equal(5f, rules.StationUpgradeRangeMeters);
        Assert.Equal(10, rules.StationSettleEveryTicks);
        Assert.Equal(512, rules.MaxTimedStationsPerConstruct);
        Assert.True(rules.StationUseRangeMeters >= rules.ContainerKeepRangeMeters);
    }
}

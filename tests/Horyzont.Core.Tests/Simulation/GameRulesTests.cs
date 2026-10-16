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

    [Fact]
    public void TimedStationsAboveProtocolCap_Throws()
    {
        // Review B1, 7: the limit protects the format of message 46, so a rule can lower it but never raise it.
        Assert.Equal(512, new GameRules { MaxTimedStationsPerConstruct = 512 }.MaxTimedStationsPerConstruct);
        Assert.Equal(1, new GameRules { MaxTimedStationsPerConstruct = 1 }.MaxTimedStationsPerConstruct);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameRules { MaxTimedStationsPerConstruct = 513 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new GameRules { MaxTimedStationsPerConstruct = -1 });
    }
}

using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Protocol;
using Horyzont.Core.Simulation;

namespace Horyzont.Core.Tests.Protocol;

/// <summary>
/// The codes of K6-31 and its limits, pinned by number and name next to <see cref="ProtocolContractTests.Enums_GoldenValues"/>.
/// </summary>
public sealed class PropProtocolContractTests
{
    private static readonly int[] _kindNumbers = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void Enums_PropValues_GoldenNumbersAndNames()
    {
        Assert.Equal(47, (int)OperationKind.PropRegrow);
        Assert.Equal(48, (int)OperationKind.PropHarvest);
        Assert.Equal(
            _kindNumbers,
            Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None).Select(k => (int)k));

        Assert.Equal(53, (int)RejectReason.ToolTierTooLow);
        Assert.Equal("ToolTierTooLow", RejectReason.ToolTierTooLow.ToString());
        Assert.Equal(45, (int)MessageType.PropStates);
        Assert.Equal("PropStates", MessageType.PropStates.ToString());
    }

    [Fact]
    public void GameRules_PropProbeBudget_IsTwiceTheHitPace()
    {
        var rules = new GameRules();

        // A legitimate player needs at most one generator lookup per hit at a new prop, so twice the pace covers a burst after a network
        // stall and no more (every lookup beyond that is an attacker's cost to the main thread).
        Assert.Equal(2 * rules.PropHitsPerSecond, rules.PropProbesPerSecond);
        Assert.Equal(2 * rules.PropHitBurst, rules.PropProbeBurst);
    }

    [Fact]
    public void GameRules_PropDefaults_FollowD22()
    {
        var rules = new GameRules();

        Assert.Equal(4, rules.PropHitsPerSecond);
        Assert.Equal(4, rules.PropHitBurst);
        Assert.Equal(8, rules.PropProbesPerSecond);
        Assert.Equal(8, rules.PropProbeBurst);
        Assert.Equal(1800, rules.PropDamageResetTicks);
        Assert.Equal(4096, rules.MaxDamagedProps);
        Assert.Equal(4096, rules.PropAnchorCacheSize);
        Assert.Equal(0.5f, rules.PropAnchorMarginMeters);
        Assert.Equal(20f, rules.PropRegrowClearanceMeters);
        Assert.Equal(60, rules.PropRegrowScanEveryTicks);
        Assert.Equal(256, rules.PropRegrowScanBatch);
        Assert.Equal(64, rules.MaxRegrowPerOperation);
        Assert.Equal(512f, rules.PropSyncEnterMeters);
        Assert.Equal(576f, rules.PropSyncLeaveMeters);

        // Leaving is further than entering (hysteresis), and a worn prop is forgotten after 30 s.
        Assert.True(rules.PropSyncLeaveMeters > rules.PropSyncEnterMeters);
        Assert.Equal(30 * rules.TickRate, rules.PropDamageResetTicks);
    }
}

using Horyzont.Core.Operations;
using Horyzont.Core.Props;
using Horyzont.Core.Simulation;
using Horyzont.Core.Tests.Props;

namespace Horyzont.Core.Tests.Protocol;

/// <summary>
/// The codes of K6-31 and its limits, pinned by number. <see cref="ProtocolContractTests.Enums_GoldenValues"/> takes the new members in step
/// B2 (it counts all members of an enum, so it cannot name them before they exist); until then these tests do it by number.
/// </summary>
public sealed class PropProtocolContractTests
{
    private static readonly int[] _kindNumbers = [1, 2, 3, 4, 5, 6, 7, 8];

    [Fact]
    public void Enums_PropValues_GoldenNumbersAndNames()
    {
        // The operations and the kinds of layout V1 exist since stage A; the reason and the message arrive in B2.
        Assert.Equal(47, (int)OperationKind.PropRegrow);
        Assert.Equal(48, (int)OperationKind.PropHarvest);
        Assert.Equal(
            _kindNumbers,
            Enum.GetValues<PropKindV1>().Where(k => k != PropKindV1.None).Select(k => (int)k));

        Assert.True(Enum.IsDefined(PropProtocol.ToolTierTooLow), "RejectReason 53 is not defined.");
        Assert.Equal("ToolTierTooLow", PropProtocol.ToolTierTooLow.ToString());
        Assert.True(Enum.IsDefined(PropProtocol.PropStatesType), "MessageType 45 is not defined.");
        Assert.Equal("PropStates", PropProtocol.PropStatesType.ToString());
    }

    [Fact]
    public void GameRules_PropDefaults_FollowD22()
    {
        var rules = new GameRules();

        Assert.Equal(4, rules.PropHitsPerSecond);
        Assert.Equal(4, rules.PropHitBurst);
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

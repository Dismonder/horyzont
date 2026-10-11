using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

/// <summary>K5-26 decision 1: a visible subject leaves above <c>ViewRange + max(hysteresis, 5 % ViewRange)</c>.</summary>
public sealed class InterestLeaveThresholdTests
{
    private static readonly PlayerId _viewer = new(1);
    private static readonly EntityId _subject = new(10);
    private static readonly FrameRef _system = FrameRef.Of(new SystemId(1));

    private static bool VisibleAt(InterestManager interest, float distance, float range)
    {
        interest.Update(
            [new InterestSubject(_subject, _system, new Vector3(distance, 0, 0), 0f, range)],
            [new InterestViewer(_viewer, _system, Vector3.Zero)]);
        return interest.IsVisible(_viewer, _subject);
    }

    [Theory]
    [InlineData(500f, 549f, 551f)]
    [InlineData(10_000f, 10_499f, 10_501f)]
    [InlineData(2_000f, 2_099f, 2_101f)]
    public void InterestManager_LeaveThreshold_MaxOfHysteresisAnd5Percent(float range, float stays, float leaves)
    {
        var interest = new InterestManager(50f);
        Assert.False(VisibleAt(interest, range + 1f, range));
        Assert.True(VisibleAt(interest, range - 1f, range));
        Assert.True(VisibleAt(interest, stays, range));
        Assert.False(VisibleAt(interest, leaves, range));
        Assert.False(VisibleAt(interest, stays, range));
    }
}

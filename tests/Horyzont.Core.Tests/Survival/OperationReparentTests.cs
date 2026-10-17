using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Tests.Serialization;

namespace Horyzont.Core.Tests.Survival;

/// <summary>K6-35 review A, P1-1: the reparent of an operation is found at the top level or as the last component of a compound.</summary>
public sealed class OperationReparentTests
{
    private static readonly EntityReparentOp _reparent = new(
        new EntityId(7), FrameRef.Of(new PlanetId(1)), 1, new Vector3(1f, 2f, 3f), Quaternion.Identity, Vector3.Zero, null, true);

    private static readonly FoodSetOp _feed = new(new PlayerId(1), default);

    // K6-35 review A, P2-1: until stage B2 gives format 2 a codec, the encoder must refuse the reset instead of dropping it silently. B2 deletes this test.
    [Fact]
    public void Write_ResetVitalsBeforeB2_Throws()
    {
        Assert.Throws<ArgumentException>(() => Kit.Encode(_reparent));
        Assert.NotEmpty(Kit.Encode(_reparent with { ResetVitals = false }));
    }

    [Fact]
    public void TryGetReparent_Standalone_ReturnsIt()
    {
        Assert.True(OperationReparent.TryGetReparent(_reparent, out var found));
        Assert.Same(_reparent, found);
    }

    [Fact]
    public void TryGetReparent_LastComponentOfACompound_ReturnsIt()
    {
        Assert.True(OperationReparent.TryGetReparent(new CompoundOp([_feed, _reparent]), out var found));
        Assert.Same(_reparent, found);
    }

    [Fact]
    public void TryGetReparent_NoReparentOrNotLast_ReturnsNothing()
    {
        Assert.False(OperationReparent.TryGetReparent(_feed, out var none));
        Assert.Null(none);
        Assert.False(OperationReparent.TryGetReparent(new CompoundOp([_feed, _feed]), out _));

        // A reparent that is not the last component is not a valid compound anyway; the helper does not look for it.
        Assert.False(OperationReparent.TryGetReparent(new CompoundOp([_reparent, _feed]), out _));
    }
}

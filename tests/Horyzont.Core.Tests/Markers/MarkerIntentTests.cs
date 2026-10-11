using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Markers;

namespace Horyzont.Core.Tests.Markers;

public sealed class MarkerIntentTests
{
    private static MarkerSetIntent New(bool shared = false, string name = "Camp") =>
        new(MarkerId.None, shared, new PlanetId(1), new Vector3(1, 4000, 2), name, MarkerIcon.Resource, MarkerColor.Cyan);

    private static void Fill(SimFixture f, PlayerId? owner, ulong first)
    {
        for (ulong i = 0; i < 256; i++)
        {
            f.Sim.ApplyServerOperation(MarkerOperationTests.Set(first + i, owner));
        }
    }

    [Fact]
    public void MarkerSetIntent_New_ResolvesNextIdAndOwner()
    {
        var f = new SimFixture();
        var player = f.Join();

        var privateResult = f.Sim.Submit(player, New());
        var sharedResult = f.Sim.Submit(player, New(shared: true));

        Assert.True(privateResult.IsAccepted);
        Assert.True(sharedResult.IsAccepted);
        var op = Assert.IsType<MarkerSetOp>(privateResult.Applied!.Operation);
        Assert.Equal(new MarkerId(1), op.Marker);
        Assert.Equal(player, op.Owner);
        Assert.Null(Assert.IsType<MarkerSetOp>(sharedResult.Applied!.Operation).Owner);
        Assert.Equal(3ul, f.State.MarkerIds.NextValue);
    }

    [Fact]
    public void MarkerSetIntent_257thPrivate_MarkerLimit()
    {
        var f = new SimFixture();
        var player = f.Join();
        Fill(f, player, 1);

        Assert.Equal(RejectReason.MarkerLimit, f.Sim.Submit(player, New()).Reason);
        Assert.True(f.Sim.Submit(player, New(shared: true)).IsAccepted);
        Assert.Equal(256, f.State.Markers.CountPrivate(player));
    }

    [Fact]
    public void MarkerSetIntent_257thShared_MarkerLimit()
    {
        var f = new SimFixture();
        var player = f.Join();
        Fill(f, null, 1);

        Assert.Equal(RejectReason.MarkerLimit, f.Sim.Submit(player, New(shared: true)).Reason);
        Assert.True(f.Sim.Submit(player, New()).IsAccepted);
    }

    [Fact]
    public void MarkerSetIntent_EditOwn_KeepsOwnerAndAllowsAtLimit()
    {
        var f = new SimFixture();
        var player = f.Join();
        Fill(f, player, 1);

        var result = f.Sim.Submit(player, New() with { Marker = new MarkerId(7), Name = "Edited" });

        Assert.True(result.IsAccepted);
        Assert.True(f.State.Markers.TryGet(new MarkerId(7), out var m));
        Assert.Equal("Edited", m.Name);
        Assert.Equal(player, m.Owner);
    }

    [Fact]
    public void MarkerSetIntent_EditPrivateOfOther_Rejected()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        var b = f.Join(2);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, a));

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(b, New() with { Marker = new MarkerId(1) }).Reason);
        // The same answer as for a marker that does not exist: no leak of foreign private markers.
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(b, New() with { Marker = new MarkerId(99) }).Reason);
        Assert.True(f.State.Markers.TryGet(new MarkerId(1), out var m));
        Assert.Equal("Base", m.Name);
    }

    [Fact]
    public void MarkerSetIntent_ChangeShared_Rejected()
    {
        var f = new SimFixture();
        var a = f.Join();
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, a));
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(2, null));

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(a, New(shared: true) with { Marker = new MarkerId(1) }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(a, New(shared: false) with { Marker = new MarkerId(2) }).Reason);
    }

    [Fact]
    public void MarkerSetIntent_SharedEditedByAnyone_Accepted()
    {
        var f = new SimFixture();
        f.Join(1);
        var b = f.Join(2);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, null));

        Assert.True(f.Sim.Submit(b, New(shared: true) with { Marker = new MarkerId(1), Name = "By B" }).IsAccepted);
        Assert.True(f.State.Markers.TryGet(new MarkerId(1), out var m));
        Assert.Null(m.Owner);
        Assert.Equal("By B", m.Name);
    }

    [Fact]
    public void MarkerSetIntent_InvalidParameters_Rejected()
    {
        var f = new SimFixture();
        var player = f.Join();

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(player, New(name: " ")).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(player, New() with { Position = new Vector3(float.PositiveInfinity, 0, 0) }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(player, New() with { Planet = new PlanetId(42) }).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(player, New() with { Icon = (MarkerIcon)9 }).Reason);
        Assert.Equal(0, f.State.Markers.Count);
    }

    [Fact]
    public void MarkerRemoveIntent_PrivateOfOther_Rejected()
    {
        var f = new SimFixture();
        var a = f.Join(1);
        var b = f.Join(2);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, a));

        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(b, new MarkerRemoveIntent(new MarkerId(1))).Reason);
        Assert.Equal(RejectReason.InvalidParameters, f.Sim.Submit(b, new MarkerRemoveIntent(new MarkerId(77))).Reason);
        Assert.True(f.Sim.Submit(a, new MarkerRemoveIntent(new MarkerId(1))).IsAccepted);
        Assert.Equal(0, f.State.Markers.Count);
    }

    [Fact]
    public void MarkerRemoveIntent_SharedByAnyone_Accepted()
    {
        var f = new SimFixture();
        var b = f.Join(2);
        f.Sim.ApplyServerOperation(MarkerOperationTests.Set(1, null));

        Assert.True(f.Sim.Submit(b, new MarkerRemoveIntent(new MarkerId(1))).IsAccepted);
    }

    [Fact]
    public void MarkerIntents_Burst_RateLimited()
    {
        var f = new SimFixture();
        var player = f.Join();

        for (var i = 0; i < 8; i++)
        {
            Assert.True(f.Sim.Submit(player, New()).IsAccepted);
        }

        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(player, New()).Reason);
        f.Ticks(15);
        Assert.True(f.Sim.Submit(player, New()).IsAccepted);
        Assert.Equal(RejectReason.RateLimited, f.Sim.Submit(player, new MarkerRemoveIntent(new MarkerId(1))).Reason);
    }

    [Fact]
    public void MarkerIntents_UnknownPlayer_Rejected()
    {
        var f = new SimFixture();

        Assert.Equal(RejectReason.UnknownPlayer, f.Sim.Submit(new PlayerId(9), New()).Reason);
    }
}

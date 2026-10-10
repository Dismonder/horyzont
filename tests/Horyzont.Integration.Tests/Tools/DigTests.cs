using System.Numerics;

using Horyzont.Client.Tools;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Net.Transport.InMemory;
using Horyzont.Testing;

using static Horyzont.Integration.Tests.Tools.ToolTestHelpers;

namespace Horyzont.Integration.Tests.Tools;

/// <summary>Terrain tool through the full path: ToolController, session, in-memory transport, server session, simulation.</summary>
public sealed class DigTests
{
    [Fact]
    public void Dig_ValidRadius_AppliesTerrainEditAtRaycastHit()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(2f));
        Assert.True(rig.Terrain.Raycast(WorldFactory.HomePlanet, aim.Eye, aim.Direction, 8f, out var hit));

        alice.Update(Input(radius: 2, primaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));

        var edit = Assert.IsType<TerrainEditOp>(rig.Journal.Entries[^1].Operation);
        Assert.Equal(RejectReason.None, alice.Replies[0].Reason);
        Assert.Equal(TerrainEditMode.Remove, edit.Mode);
        Assert.Equal(0, edit.MaterialId);
        Assert.Equal(hit.Position, edit.Center);
        Assert.Equal(alice.Controller.Settings.Radius, edit.Radius);
        Assert.Equal(1.5f, edit.Radius);
        Assert.Equal(0, alice.Tracker.Count);
    }

    [Fact]
    public void Dig_RadiusOutsideLimits_RejectedInvalidParameters()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var target = rig.Ground(2f);

        Assert.Equal(RejectReason.InvalidParameters, Raw(rig, alice, Dig(rig, alice, target, 0.49f)));
        Assert.Equal(RejectReason.InvalidParameters, Raw(rig, alice, Dig(rig, alice, target, 3.01f)));
        Assert.Equal(RejectReason.None, Raw(rig, alice, Dig(rig, alice, target, 0.5f)));
        Assert.Equal(RejectReason.None, Raw(rig, alice, Dig(rig, alice, target, 3f)));
    }

    [Fact]
    public void Dig_TargetBeyondReach_RejectedNoHit()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);

        var far = GroundAtDistance(rig, alice.Eye, 8.1f);

        Assert.Equal(RejectReason.NoHit, Raw(rig, alice, Dig(rig, alice, far)));
        Assert.Equal(RejectReason.None, Raw(rig, alice, Dig(rig, alice, GroundAtDistance(rig, alice.Eye, 7.9f))));
    }

    [Fact]
    public void Dig_ShiftedRayOrigin_HitBeyondEyeReach_RejectedOutOfReach()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);

        // The origin is 1.9 m ahead of the eye (inside the 2 m tolerance): the hit is 7.5 m from the origin but 9.4 m from the eye.
        var beyond = Dig(rig, alice, GroundAtDistance(rig, alice.Eye, 9.4f), originShift: 1.9f);
        var within = Dig(rig, alice, GroundAtDistance(rig, alice.Eye, 7.9f), originShift: 1.9f);

        Assert.Equal(RejectReason.OutOfReach, Raw(rig, alice, beyond));
        Assert.Equal(RejectReason.None, Raw(rig, alice, within));
    }

    [Fact]
    public void PlaceOnTerrain_ShiftedRayOrigin_RejectedOutOfReach()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);

        var beyond = PlaceOnTerrain(rig, alice, GroundAtDistance(rig, alice.Eye, 9.4f), originShift: 1.9f);
        var within = PlaceOnTerrain(rig, alice, GroundAtDistance(rig, alice.Eye, 7.9f), originShift: 1.9f);

        Assert.Equal(RejectReason.OutOfReach, Raw(rig, alice, beyond));
        Assert.Equal(RejectReason.None, Raw(rig, alice, within));
    }

    [Fact]
    public void Dig_BelowCore_RejectedBelowCore()
    {
        using var rig = new ToolRig(surfaceRadius: 3500f);
        var alice = rig.AddClient("alice", 1);
        var before = rig.Journal.Entries.Count;

        Assert.Equal(RejectReason.BelowCore, Raw(rig, alice, Dig(rig, alice, rig.Ground(2f))));

        Assert.Equal(before, rig.Journal.Entries.Count);
    }

    [Fact]
    public void Dig_HoldTenSeconds_NoRateLimitedReplies()
    {
        var link = new LinkConditions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), 0, 0, 7);
        using var rig = new ToolRig(link: link);
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(2f));
        var first = (ulong)rig.Tick;

        for (var i = 0; i < 600; i++)
        {
            alice.Update(Input(primaryPressed: i == 0, primaryHeld: true), aim);
            rig.Pump();
        }

        rig.Pump(30);
        var ticks = EditTicks(rig);
        Assert.InRange(ticks.Count, 40, 50);
        AssertEveryWindowAtMost(ticks, first, 600, 60, 5);
        Assert.DoesNotContain(alice.Replies, r => r.Reason == RejectReason.RateLimited);
        Assert.All(alice.Replies, r => Assert.Equal(RejectReason.None, r.Reason));
        Assert.Equal(0, alice.Tracker.Count);
    }

    [Fact]
    public void Dig_RawFloodBypassingPacer_SixthWithinOneSecondRateLimited()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var dig = Dig(rig, alice, rig.Ground(2f));

        var burst = Enumerable.Range(0, 6).Select(_ => alice.SubmitRaw(dig)).ToList();
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 6));
        var firstTick = EditTicks(rig)[0];

        Assert.Equal(5, burst.Take(5).Count(seq => alice.Replies.Find(r => r.ClientSeq == seq)!.Reason == RejectReason.None));
        Assert.Equal(RejectReason.RateLimited, alice.Replies.Find(r => r.ClientSeq == burst[5])!.Reason);

        // One attempt per tick until the window passes: all are limited, the first one at tick + 60 is accepted.
        var attempts = new List<(uint Seq, long SentAtTick)>();
        while ((ulong)rig.Tick < firstTick + 70)
        {
            attempts.Add((alice.SubmitRaw(dig), rig.Tick));
            rig.Pump();
        }

        Assert.True(rig.RunUntil(() => alice.Replies.Count == 6 + attempts.Count));
        var accepted = EditTicks(rig).Skip(5).ToList();
        Assert.NotEmpty(accepted);
        Assert.True(accepted[0] >= firstTick + 60, $"the sixth edit came at tick {accepted[0]}, the first five at {firstTick}");
        Assert.All(
            attempts.Select(a => alice.Replies.Find(r => r.ClientSeq == a.Seq)!.Reason),
            reason => Assert.True(reason is RejectReason.RateLimited or RejectReason.None));
        Assert.Contains(attempts, a => alice.Replies.Find(r => r.ClientSeq == a.Seq)!.Reason == RejectReason.RateLimited);
        AssertEveryWindowAtMost(EditTicks(rig), firstTick, 70, 60, 5);
    }

    [Fact]
    public void Dig_RawFloodTenSeconds_EveryWindowAtMostFive()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var dig = Dig(rig, alice, rig.Ground(2f));
        var first = (ulong)rig.Tick;

        for (var i = 0; i < 600; i++)
        {
            alice.SubmitRaw(dig);
            rig.Pump();
        }

        Assert.True(rig.RunUntil(() => alice.Replies.Count == 600));
        var ticks = EditTicks(rig);
        AssertEveryWindowAtMost(ticks, first, 600, 60, 5);
        Assert.InRange(ticks.Count, 45, 55);
        Assert.All(alice.Replies, r => Assert.True(r.Reason is RejectReason.None or RejectReason.RateLimited));
        Assert.Equal(ticks.Count, alice.Replies.Count(r => r.Reason == RejectReason.None));
    }

    [Fact]
    public void Fill_SelectedMaterial_OpCarriesMaterial()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(4f));
        alice.Update(Input(material: 1), aim);

        alice.Update(Input(secondaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));

        var edit = Assert.IsType<TerrainEditOp>(rig.Journal.Entries[^1].Operation);
        Assert.Equal(TerrainEditMode.Add, edit.Mode);
        Assert.Equal(alice.Controller.Settings.FillMaterial, edit.MaterialId);
        Assert.Equal(2, edit.MaterialId);
        Assert.Equal(RejectReason.None, alice.Replies[0].Reason);
    }

    [Fact]
    public void Fill_UnknownMaterial_Rejected()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var fill = Dig(rig, alice, rig.Ground(4f)) with { Mode = TerrainEditMode.Add, MaterialId = 99 };

        Assert.Equal(RejectReason.UnknownMaterial, Raw(rig, alice, fill));
    }

    [Fact]
    public void Fill_OverAnyOnlinePlayer_RejectedIntersectsPlayer()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var bob = rig.AddClient("bob", 2);
        var bobFeet = rig.Ground(3f) + new Vector3(0f, 0.1f, 0f);
        var state = bob.State;
        Assert.True(rig.Sim.UpdatePlayerKinematics(bob.Player, state.Frame, state.ParentSeq, bobFeet, Quaternion.Identity, 0f));
        var aim = alice.AimAt(rig.Ground(3f));
        alice.Update(Input(radius: 0), aim);

        alice.Update(Input(secondaryPressed: true), aim);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));

        Assert.Equal(RejectReason.IntersectsPlayer, alice.Replies[0].Reason);
        Assert.Equal(RejectReason.IntersectsPlayer, alice.Tracker.LastRejection);
        Assert.Equal(RejectReason.IntersectsPlayer, alice.Controller.Preview.Hint);
    }

    [Fact]
    public void Reject_ClientReportsReasonAndTrackerEmpty()
    {
        using var rig = new ToolRig(surfaceRadius: 3500f);
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(2f));

        alice.Update(Input(primaryPressed: true), aim);
        Assert.Equal(1, alice.Tracker.Count);
        Assert.True(rig.RunUntil(() => alice.Replies.Count == 1));

        Assert.Equal(RejectReason.BelowCore, alice.Replies[0].Reason);
        Assert.Equal(alice.Replies[0].Reason, alice.Tracker.LastRejection);
        Assert.Equal(0, alice.Tracker.Count);
        Assert.Equal(PendingIntentOutcome.Rejected, alice.Tracker.LastOutcome);
        Assert.Contains("revert", alice.Prediction.Calls[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void Disconnect_WithPendingIntents_TrackerClearedNewestFirst()
    {
        using var rig = new ToolRig();
        var alice = rig.AddClient("alice", 1);
        var aim = alice.AimAt(rig.Ground(2f));
        for (var i = 0; i < 3; i++)
        {
            alice.Update(Input(primaryPressed: true), aim);
        }

        Assert.Equal(3, alice.Tracker.Count);
        alice.Runtime.Session.Disconnect();
        Assert.True(rig.RunUntil(() => alice.Tracker.Count == 0));

        Assert.Equal(PendingIntentOutcome.Disconnected, alice.Tracker.LastOutcome);
        var reverts = alice.Prediction.Calls.Where(c => c.StartsWith("revert", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, reverts.Count);
        var seqs = reverts.Select(c => uint.Parse(c["revert ".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(seqs.OrderByDescending(s => s).ToList(), seqs);
    }
}

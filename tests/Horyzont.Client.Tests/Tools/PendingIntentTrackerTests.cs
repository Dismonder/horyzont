using Horyzont.Client.Tests.Movement;
using Horyzont.Client.Tools;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Logging;

namespace Horyzont.Client.Tests.Tools;

public sealed class PendingIntentTrackerTests
{
    private static readonly Intent _intent = new RemoveBlockIntent(new EntityId(1), new CellPos(0, 0, 0));
    private static readonly ToolPreview _preview = new(ToolPreviewKind.None, default, 0f, default, System.Numerics.Quaternion.Identity, default, default, 0, RejectReason.None);

    private sealed class Rig
    {
        public Rig()
        {
            Tracker = new PendingIntentTracker(Prediction, Log);
        }

        public RecordingPrediction Prediction { get; } = new();

        public ListLogger<PendingIntentTracker> Log { get; } = new();

        public PendingIntentTracker Tracker { get; }

        public void Add(uint seq, long tick = 0) => Tracker.Add(seq, _intent, _preview, tick);
    }

    private sealed class RecordingPrediction : IEditPrediction
    {
        public List<string> Calls { get; } = [];

        public void Begin(uint clientSeq, Intent intent, in ToolPreview preview) => Calls.Add($"begin {clientSeq}");

        public void Commit(uint clientSeq) => Calls.Add($"commit {clientSeq}");

        public void Revert(uint clientSeq) => Calls.Add($"revert {clientSeq}");
    }

    [Fact]
    public void PendingIntentTracker_Reply_CommitsOrRevertsAndReportsReason()
    {
        var rig = new Rig();
        rig.Add(1);
        rig.Add(2);

        Assert.True(rig.Tracker.OnReply(new IntentReply(1, RejectReason.None, new OpId(7)), out var accepted));
        Assert.True(rig.Tracker.OnReply(new IntentReply(2, RejectReason.BelowCore, OpId.None), out var rejected));

        Assert.Equal(PendingIntentOutcome.Accepted, accepted);
        Assert.Equal(PendingIntentOutcome.Rejected, rejected);
        Assert.Equal(RejectReason.BelowCore, rig.Tracker.LastRejection);
        Assert.Equal(["begin 1", "begin 2", "commit 1", "revert 2"], rig.Prediction.Calls);
        Assert.Equal(0, rig.Tracker.Count);
        Assert.Equal(PendingIntentOutcome.Rejected, rig.Tracker.LastOutcome);
        Assert.Equal(2u, rig.Tracker.LastOutcomeSeq);
        Assert.Contains(rig.Log.Entries, e => e.EventId == 9201 && e.Message.Contains("BelowCore", StringComparison.Ordinal));
    }

    [Fact]
    public void PendingIntentTracker_NoReplyWithinDeadline_RevertsAndLogs()
    {
        var rig = new Rig();
        rig.Add(1, tick: 100);
        rig.Add(2, tick: 150);

        rig.Tracker.Tick(100 + PendingIntentTracker.DeadlineTicks - 1);
        Assert.Equal(2, rig.Tracker.Count);

        rig.Tracker.Tick(100 + PendingIntentTracker.DeadlineTicks);
        Assert.Equal(1, rig.Tracker.Count);
        Assert.Equal(PendingIntentOutcome.TimedOut, rig.Tracker.LastOutcome);
        Assert.Equal(1u, rig.Tracker.LastOutcomeSeq);
        Assert.Contains(rig.Log.Entries, e => e.Level == LogLevel.Warning && e.EventId == 9203 && e.Message.Contains("intent 1", StringComparison.Ordinal));

        rig.Tracker.Tick(450);

        Assert.Equal(0, rig.Tracker.Count);
        Assert.Equal(["begin 1", "begin 2", "revert 1", "revert 2"], rig.Prediction.Calls);
    }

    [Fact]
    public void PendingIntentTracker_ManyExpireAtOnce_RevertNewestFirst()
    {
        var rig = new Rig();
        for (uint seq = 1; seq <= 4; seq++)
        {
            rig.Add(seq);
        }

        rig.Tracker.Tick(PendingIntentTracker.DeadlineTicks);

        Assert.Equal(["begin 1", "begin 2", "begin 3", "begin 4", "revert 4", "revert 3", "revert 2", "revert 1"], rig.Prediction.Calls);
    }

    [Fact]
    public void PendingIntentTracker_Full_RefusesLocally()
    {
        var rig = new Rig();
        for (uint seq = 1; seq <= PendingIntentTracker.MaxPending; seq++)
        {
            Assert.False(rig.Tracker.IsFull);
            rig.Add(seq);
        }

        Assert.True(rig.Tracker.IsFull);
        Assert.Equal(PendingIntentTracker.MaxPending, rig.Tracker.Count);
        Assert.Throws<InvalidOperationException>(() => rig.Add(99));
        Assert.True(rig.Tracker.OnReply(new IntentReply(1, RejectReason.None, new OpId(1)), out _));
        Assert.False(rig.Tracker.IsFull);
    }

    [Fact]
    public void PendingIntentTracker_Clear_RevertsNewestFirst()
    {
        var rig = new Rig();
        rig.Add(1);
        rig.Add(2);
        rig.Add(3);

        rig.Tracker.Clear();

        Assert.Equal(["begin 1", "begin 2", "begin 3", "revert 3", "revert 2", "revert 1"], rig.Prediction.Calls);
        Assert.Equal(0, rig.Tracker.Count);
        Assert.Equal(PendingIntentOutcome.Disconnected, rig.Tracker.LastOutcome);
        rig.Tracker.Clear();
        Assert.Equal(6, rig.Prediction.Calls.Count);
    }

    [Fact]
    public void PendingIntentTracker_UnknownOrDuplicateSeq_IgnoredWithoutThrow()
    {
        var rig = new Rig();
        rig.Add(1);
        rig.Add(2);

        Assert.False(rig.Tracker.OnReply(new IntentReply(99, RejectReason.None, new OpId(1)), out _));
        Assert.True(rig.Tracker.OnReply(new IntentReply(1, RejectReason.None, new OpId(2)), out _));
        Assert.False(rig.Tracker.OnReply(new IntentReply(1, RejectReason.NoHit, OpId.None), out _));

        Assert.Equal(1, rig.Tracker.Count);
        Assert.Equal(RejectReason.None, rig.Tracker.LastRejection);
        Assert.Equal(["begin 1", "begin 2", "commit 1"], rig.Prediction.Calls);
        Assert.Equal(2, rig.Log.Entries.Count(e => e.Level == LogLevel.Warning && e.EventId == 9202));
    }

    [Fact]
    public void PendingIntentTracker_SequenceNotIncreasing_Throws()
    {
        var rig = new Rig();
        rig.Add(5);

        Assert.Throws<InvalidOperationException>(() => rig.Add(5));
    }
}

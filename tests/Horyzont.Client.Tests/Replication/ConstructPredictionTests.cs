using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Client.Tools;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

public sealed class ConstructPredictionTests
{
    private static readonly EntityId _construct = new(5);
    private static readonly BlockState _stone = BlockState.Create(1, 0);

    private static ConstructHeaderRecord Header => new(_construct, FrameRef.Of(new PlanetId(1)), 0, ConstructMode.Static,
        Vector3.Zero, Quaternion.Identity, 1);

    private static ToolPreview Preview(CellPos cell) =>
        new(ToolPreviewKind.ConstructCell, default, 0, default, Quaternion.Identity, _construct, cell, 0, RejectReason.None);

    private static (ConstructMirror Mirror, ConstructPrediction Prediction, Func<long> Clock, Action<long> Advance) Setup()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        Assert.True(mirror.Apply(new ConstructState(Header, 0, 1, [])));
        var ticks = 0L;
        return (mirror, new ConstructPrediction(mirror, () => ticks), () => ticks, t => ticks = t);
    }

    private static OperationApplied Op(ulong id, uint version, CellPos cell, bool clear = false) =>
        new(new OpId(id), default, clear ? new BlockClearOp(_construct, cell) : new BlockSetOp(_construct, cell, _stone), [new(_construct, version, false)]);

    [Fact]
    public void Prediction_ReplyBeforeAndAfterOperation_RemovesOverlayOnce()
    {
        var (mirror, prediction, _, _) = Setup();
        var removed = 0;
        prediction.OverlayChanged += () => removed++;
        var cell = new CellPos(1, 0, 0);

        // Reply first, operation later.
        prediction.Begin(1, new PlaceBlockIntent(new ConstructFaceTarget(_construct, default, Face.PosX), 1, 0), Preview(cell));
        Assert.Equal(1, prediction.Count);
        prediction.NoteReply(new IntentReply(1, RejectReason.None, new OpId(70)));
        prediction.Commit(1);
        Assert.Equal(1, prediction.Count);
        Assert.True(mirror.Apply(Op(70, 2, cell)));
        Assert.Equal(0, prediction.Count);

        // Operation first, reply later.
        var cell2 = new CellPos(2, 0, 0);
        prediction.Begin(2, new PlaceBlockIntent(new ConstructFaceTarget(_construct, default, Face.PosX), 1, 0), Preview(cell2));
        Assert.True(mirror.Apply(Op(71, 3, cell2)));
        Assert.Equal(1, prediction.Count);
        prediction.NoteReply(new IntentReply(2, RejectReason.None, new OpId(71)));
        prediction.Commit(2);
        Assert.Equal(0, prediction.Count);

        // A repeated reply or operation changes nothing; each entry raised exactly one add and one remove.
        prediction.Commit(2);
        prediction.Revert(2);
        mirror.Apply(Op(71, 3, cell2));
        Assert.Equal(4, removed);
    }

    [Fact]
    public void Prediction_RejectTimeoutAndDisconnect_RevertsNewestFirstWithoutTouchingMirror()
    {
        var (mirror, prediction, _, advance) = Setup();
        var cell = new CellPos(1, 0, 0);
        var intent = new RemoveBlockIntent(_construct, cell);
        prediction.Begin(1, intent, Preview(cell));
        prediction.Begin(2, intent, Preview(cell));
        prediction.Begin(3, intent, Preview(cell));
        Assert.Equal([1u, 2u, 3u], prediction.Entries.Select(e => e.ClientSeq).ToArray());
        prediction.NoteReply(new IntentReply(2, RejectReason.OutOfReach, OpId.None));
        prediction.Revert(2);
        Assert.Equal([1u, 3u], prediction.Entries.Select(e => e.ClientSeq).ToArray());
        advance(ConstructPrediction.DeadlineTicks);
        Assert.Equal(0, prediction.Count);
        Assert.True(mirror.TryGetConstruct(_construct, out var value));
        Assert.Equal(1u, value.Version);
        Assert.Equal(0, value.BlockCount);
    }

    [Fact]
    public void Prediction_FullStateAfterAcceptedReply_RemovesOverlayWhenCellMatches()
    {
        var (mirror, prediction, _, _) = Setup();
        var cell = new CellPos(3, 0, 0);
        prediction.Begin(1, new PlaceBlockIntent(new ConstructFaceTarget(_construct, default, Face.PosX), 1, 0), Preview(cell));
        prediction.NoteReply(new IntentReply(1, RejectReason.None, new OpId(90)));
        prediction.Commit(1);
        Assert.Equal(1, prediction.Count);

        // A state that does not show the cell keeps the overlay; one that does removes it.
        mirror.Apply(new ConstructState(Header with { Version = 4 }, 0, 1, []));
        Assert.Equal(1, prediction.Count);
        var blocks = new uint[4096];
        blocks[cell.LocalIndex] = _stone.Packed;
        mirror.Apply(new ConstructState(Header with { Version = 5 }, 0, 1, [new(new(_construct, default), blocks)]));
        Assert.Equal(0, prediction.Count);
    }

    [Fact]
    public void Prediction_NewConstructAndLimit_GhostStaysAndNeverExceedsSixteen()
    {
        var (_, prediction, _, _) = Setup();
        var origin = new Vector3(0, 4000, 5);
        var ghost = new ToolPreview(ToolPreviewKind.NewConstructCell, default, 0, origin, Quaternion.Identity, default, default, 0, RejectReason.None);
        var target = new TerrainTarget(new PlanetId(1), origin, Vector3.UnitY, Vector3.UnitZ);
        prediction.Begin(1, new PlaceBlockIntent(target, 1, 0), ghost);
        var entry = Assert.Single(prediction.Entries);
        Assert.True(entry.IsNewConstruct);
        Assert.Equal(origin, entry.Origin);
        for (uint seq = 2; seq < 40; seq++)
        {
            prediction.Begin(seq, new RemoveBlockIntent(_construct, default), Preview(default));
        }

        Assert.Equal(ConstructPrediction.MaxEntries, prediction.Count);
    }

    [Fact]
    public void Composite_RoutesBlocksAndTerrainAndForwardsReplies()
    {
        var (_, blocks, _, _) = Setup();
        var terrain = new RecordingPrediction();
        var composite = new CompositeEditPrediction(blocks, terrain);
        composite.Begin(1, new RemoveBlockIntent(_construct, default), Preview(default));
        composite.Begin(2, new TerrainToolIntent(new PlanetId(1), Vector3.Zero, Vector3.UnitX, 1f, TerrainEditMode.Remove, 0), default);
        Assert.Equal(1, blocks.Count);
        Assert.Equal(["begin 2"], terrain.Log);
        composite.NoteReply(new IntentReply(2, RejectReason.None, new OpId(5)));
        composite.Commit(2);
        composite.Revert(1);
        Assert.Equal(["begin 2", "reply 2", "commit 2"], terrain.Log);
        Assert.Equal(0, blocks.Count);
    }

    private sealed class RecordingPrediction : IEditPrediction, IIntentReplyObserver
    {
        public List<string> Log { get; } = [];

        public void Begin(uint clientSeq, Intent intent, in ToolPreview preview) => Log.Add("begin " + clientSeq);

        public void Commit(uint clientSeq) => Log.Add("commit " + clientSeq);

        public void Revert(uint clientSeq) => Log.Add("revert " + clientSeq);

        public void NoteReply(IntentReply reply) => Log.Add("reply " + reply.ClientSeq);
    }
}

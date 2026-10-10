using System.Numerics;

using Horyzont.Client.Terrain;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Serialization;
using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Terrain;

public sealed class TerrainReplicationHelpersTests
{
    private static readonly PlanetId Planet = new(1);

    [Fact]
    public void ViewReporter_FirstReportIsImmediate_ThenOnlyOnChangeAtFiveHertz()
    {
        var time = new FakeTimeProvider();
        var reporter = new TerrainViewReporter(time);
        var viewers = new TerrainViewers(Planet, new Vector3(100f, 4000f, 0f), 2048, new Vector3(100f, 4000f, 0f), 128);

        Assert.True(reporter.TryCreate(viewers, out var first));
        Assert.Equal(1u, first.ViewSeq);
        Assert.False(reporter.TryCreate(viewers, out _));

        time.Advance(TimeSpan.FromMilliseconds(100));
        var moved = viewers with { CollisionPosition = new Vector3(300f, 4000f, 0f) };
        Assert.False(reporter.TryCreate(moved, out _));

        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(reporter.TryCreate(moved, out var second));
        Assert.Equal(2u, second.ViewSeq);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(reporter.TryCreate(moved with { CollisionPosition = new Vector3(301f, 4000f, 0f) }, out _));
        Assert.True(reporter.TryCreate(moved with { VisualDistance = 4096 }, out var third));
        Assert.Equal(4096f, third.VisualDistance);
    }

    [Fact]
    public void ViewReporter_NonFinitePosition_IsNotReported()
    {
        var reporter = new TerrainViewReporter(new FakeTimeProvider());
        Assert.False(reporter.TryCreate(new TerrainViewers(Planet, new Vector3(float.NaN, 0f, 0f), 1, Vector3.Zero, 1), out _));
    }

    [Fact]
    public void ChecksumScheduler_RotatesThroughAllCandidatesEveryFiveSeconds()
    {
        var time = new FakeTimeProvider();
        var scheduler = new TerrainChecksumScheduler(time);
        var candidates = Enumerable.Range(0, 80).Select(i => new TerrainBlockKey(Planet, 0, i, 0, 0)).ToList();
        var batch = new List<TerrainBlockKey>();
        var seen = new HashSet<TerrainBlockKey>();

        for (var round = 0; round < 3; round++)
        {
            Assert.True(scheduler.TryTake(candidates, batch));
            Assert.True(batch.Count <= TerrainChecksumScheduler.BatchSize);
            seen.UnionWith(batch);
            Assert.False(scheduler.TryTake(candidates, batch));
            time.Advance(TimeSpan.FromSeconds(5));
        }

        Assert.Equal(80, seen.Count);
        Assert.True(scheduler.TryTake(candidates, batch));
        Assert.Equal(candidates[16], batch[0]);
    }

    [Fact]
    public void CacheReports_KeepNearestKeys_InMessagesTheCodecAccepts()
    {
        var versions = Enumerable.Range(0, 70_000)
            .Select(i => new TerrainBlockVersion(new TerrainBlockKey(Planet, 0, i % 500, i / 500, 0), 1))
            .ToArray();

        var reports = TerrainCacheReports.Build(Planet, versions, Vector3.Zero, 1f);

        Assert.Equal(TerrainCacheReports.MaxKeysPerPlanet, reports.Sum(r => r.Entries.Count));
        Assert.All(reports, r => Assert.True(r.Entries.Count <= ProtocolConstants.MaxTerrainCacheEntries));
        Assert.Equal(new TerrainBlockKey(Planet, 0, 0, 0, 0), reports[0].Entries[0].Key);
        var writer = new ByteWriter();
        MessageCodec.Write(writer, reports[0]);
        Assert.Empty(TerrainCacheReports.Build(Planet, [], Vector3.Zero, 1f));
    }

    [Fact]
    public void Ledger_AcceptedEditLeavesOnlyWithItsBarrier_InEitherOrder()
    {
        var time = new FakeTimeProvider();
        var ledger = new EditPredictionLedger(time);
        var key = new TerrainBlockKey(Planet, 0, 0, 0, 0);
        PredictedEdit Make(uint seq) => new(seq, Planet, Vector3.Zero, 1f, TerrainEditMode.Remove, 0, [key]);

        Assert.True(ledger.TryBegin(Make(1)));
        Assert.False(ledger.NoteAccepted(1, new OpId(10), out _));
        Assert.Equal(1, ledger.Count);
        Assert.NotNull(ledger.Complete(new OpId(10)));
        Assert.Equal(0, ledger.Count);

        Assert.True(ledger.TryBegin(Make(2)));
        Assert.Null(ledger.Complete(new OpId(11)));
        Assert.True(ledger.NoteAccepted(2, new OpId(11), out var done));
        Assert.Equal(2u, done!.ClientSeq);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void Ledger_RejectionKeepsTheOthers_AndLimitsPending()
    {
        var ledger = new EditPredictionLedger(new FakeTimeProvider());
        var key = new TerrainBlockKey(Planet, 0, 0, 0, 0);
        for (uint seq = 1; seq <= EditPredictionLedger.MaxEdits; seq++)
        {
            Assert.True(ledger.TryBegin(new PredictedEdit(seq, Planet, Vector3.Zero, 1f, TerrainEditMode.Add, 1, [key])));
        }

        Assert.False(ledger.TryBegin(new PredictedEdit(99, Planet, Vector3.Zero, 1f, TerrainEditMode.Add, 1, [key])));
        Assert.NotNull(ledger.Remove(3));
        var touching = new List<PredictedEdit>();
        ledger.CollectTouching(key, touching);
        Assert.Equal(EditPredictionLedger.MaxEdits - 1, touching.Count);
        Assert.Equal(touching.Select(e => e.ClientSeq).Order(), touching.Select(e => e.ClientSeq));
    }

    [Fact]
    public void Ledger_AcceptedEditWithoutBarrier_ExpiresAndSessionEndTakesNewestFirst()
    {
        var time = new FakeTimeProvider();
        var ledger = new EditPredictionLedger(time);
        var key = new TerrainBlockKey(Planet, 0, 0, 0, 0);
        ledger.TryBegin(new PredictedEdit(1, Planet, Vector3.Zero, 1f, TerrainEditMode.Remove, 0, [key]));
        ledger.TryBegin(new PredictedEdit(2, Planet, Vector3.Zero, 1f, TerrainEditMode.Remove, 0, [key]));
        ledger.NoteAccepted(1, new OpId(5), out _);
        time.Advance(TimeSpan.FromSeconds(6));
        var expired = new List<PredictedEdit>();

        ledger.TakeExpired(TimeSpan.FromSeconds(5), expired);

        Assert.Equal([1u], expired.Select(e => e.ClientSeq));
        ledger.TryBegin(new PredictedEdit(3, Planet, Vector3.Zero, 1f, TerrainEditMode.Remove, 0, [key]));
        ledger.TakeAll(expired);
        Assert.Equal([3u, 2u], expired.Select(e => e.ClientSeq));
        Assert.Equal(0, ledger.Count);
    }
}

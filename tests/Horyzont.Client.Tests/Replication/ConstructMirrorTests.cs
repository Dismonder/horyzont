using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

public sealed class ConstructMirrorTests
{
    private static ConstructHeaderRecord Header => new(new EntityId(5), FrameRef.Of(new PlanetId(1)), 0,
        ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1);

    [Fact]
    public void Apply_MultipartConstructThenEdits_PublishesCompleteVersion()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        var chunks = new List<ConstructChunkRecord>();
        for (var i = 0; i < 13; i++)
        {
            var blocks = new uint[4096];
            blocks[0] = 1;
            chunks.Add(new(new(Header.Id, new ChunkPos(i, 0, 0)), blocks));
        }

        Assert.False(mirror.Apply(new ConstructState(Header, 0, 2, chunks.GetRange(0, 12))));
        Assert.False(mirror.TryGetConstruct(Header.Id, out _));
        Assert.True(mirror.Apply(new ConstructState(Header, 1, 2, chunks.GetRange(12, 1))));
        Assert.True(mirror.TryGetConstruct(Header.Id, out var value));
        Assert.Equal(13, value.ChunkCount);
        Assert.True(mirror.Apply(new OperationApplied(new OpId(55), default,
            new BlockSetOp(Header.Id, new CellPos(1, 0, 0), BlockState.Create(1, 0)), [new(Header.Id, 2, false)])));
        Assert.Equal(2u, value.Version);
        mirror.Remove(Header.Id);
        Assert.False(mirror.TryGetConstruct(Header.Id, out _));
        Assert.True(mirror.Apply(new ConstructState(Header, 0, 1, [])));
        Assert.True(mirror.TryGetConstruct(Header.Id, out value));
        Assert.Equal(0, value.BlockCount);
    }

    [Fact]
    public void Apply_MissingPartDuplicateChunkAndStagingLimit_ResyncsWithoutPartialState()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        var chunk = new ConstructChunkRecord(new(Header.Id, default), new uint[4096]);
        Assert.False(mirror.Apply(new ConstructState(Header, 1, 2, [chunk])));
        Assert.Single(requests);
        Assert.False(mirror.Apply(new ConstructState(Header, 0, 2, [chunk])));
        Assert.False(mirror.Apply(new ConstructState(Header, 1, 2, [chunk])));
        Assert.False(mirror.TryGetConstruct(Header.Id, out _));
        mirror.Apply(new ConstructState(Header, 0, 2, [chunk]));
        time.Advance(TimeSpan.FromSeconds(5));
        mirror.ExpireTransfers();
        Assert.Equal(0, mirror.StagingBytes);
        Assert.False(mirror.TryGetConstruct(Header.Id, out _));
    }

    [Fact]
    public void Apply_VersionGapAndDuplicate_RequestsOneResync()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        mirror.Apply(new ConstructState(Header, 0, 1, []));
        var edit = new OperationApplied(new OpId(100), default, new BlockSetOp(Header.Id, default, BlockState.Create(1, 0)), [new(Header.Id, 3, false)]);
        Assert.False(mirror.Apply(edit));
        Assert.False(mirror.Apply(edit));
        Assert.Single(requests);
        mirror.Apply(new ConstructState(Header with { Version = 3 }, 0, 1, []));
        Assert.False(mirror.Apply(edit));
        Assert.True(mirror.Apply(edit with { Id = new OpId(200), ConstructChanges = [new(Header.Id, 4, false)] }));
        Assert.True(mirror.TryGetConstruct(Header.Id, out var value));
        Assert.Equal(4u, value.Version);
    }

    private static ConstructChunkRecord Chunk(int x) => new(new(Header.Id, new ChunkPos(x, 0, 0)), new uint[4096]);

    [Fact]
    public void Apply_SlowTransferThatKeepsProgressing_IsNotExpired()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        Assert.False(mirror.Apply(new ConstructState(Header, 0, 4, [Chunk(0)])));
        for (ushort part = 1; part < 3; part++)
        {
            time.Advance(TimeSpan.FromSeconds(4));
            Assert.False(mirror.Apply(new ConstructState(Header, part, 4, [Chunk(part)])));
        }

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(mirror.Apply(new ConstructState(Header, 3, 4, [Chunk(3)])));
        Assert.Empty(requests);
        Assert.Equal(0, mirror.StagingBytes);
    }

    [Fact]
    public void Apply_StalledTransferThenLeftoverParts_RequestsOnceAndIgnoresLeftoversWithoutDisconnect()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        mirror.Apply(new ConstructState(Header, 0, 40, [Chunk(0)]));
        time.Advance(TimeSpan.FromSeconds(5));
        mirror.ExpireTransfers();
        Assert.Single(requests);
        for (ushort part = 1; part < 30; part++)
        {
            Assert.False(mirror.Apply(new ConstructState(Header, part, 40, [Chunk(part)])));
        }

        Assert.Single(requests);
        Assert.Equal(0, mirror.StagingBytes);
        Assert.True(mirror.Apply(new ConstructState(Header, 0, 1, [])));
    }

    [Fact]
    public void ExpireTransfers_UnansweredResync_IsRepeatedWithBackoff()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        var edit = new OperationApplied(new OpId(100), default, new BlockSetOp(Header.Id, default, BlockState.Create(1, 0)), [new(Header.Id, 3, false)]);
        mirror.Apply(new ConstructState(Header, 0, 1, []));
        mirror.Apply(edit);
        Assert.Single(requests);
        time.Advance(TimeSpan.FromSeconds(1));
        mirror.ExpireTransfers();
        Assert.Single(requests);
        time.Advance(TimeSpan.FromSeconds(1));
        mirror.ExpireTransfers();
        Assert.Equal(2, requests.Count);
        time.Advance(TimeSpan.FromSeconds(3));
        mirror.ExpireTransfers();
        Assert.Equal(2, requests.Count);
        time.Advance(TimeSpan.FromSeconds(1));
        mirror.ExpireTransfers();
        Assert.Equal(3, requests.Count);

        // An answer (part 0) ends the repetition.
        mirror.Apply(new ConstructState(Header, 0, 1, []));
        time.Advance(TimeSpan.FromMinutes(1));
        mirror.ExpireTransfers();
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public void Apply_InvalidPartsSpreadOverTime_DoNotDisconnect()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        for (var i = 0; i < 40; i++)
        {
            mirror.Apply(new ConstructState(Header, 0, 2, [Chunk(0), Chunk(0)]));
            time.Advance(TimeSpan.FromSeconds(31));
        }
    }

    [Fact]
    public void ExpireTransfers_StallAfterPartZeroOfAnAnsweredResync_RepeatsTheRequest()
    {
        var time = new FakeTimeProvider();
        var mirror = new ConstructMirror(time);
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        Assert.False(mirror.Apply(new ConstructState(Header, 1, 2, [Chunk(1)])));
        Assert.Single(requests);
        Assert.False(mirror.Apply(new ConstructState(Header, 0, 3, [Chunk(0)])));
        time.Advance(TimeSpan.FromSeconds(5));
        mirror.ExpireTransfers();
        Assert.Equal(2, requests.Count);
        Assert.Equal(0, mirror.StagingBytes);
    }

    [Fact]
    public void Apply_InvalidPartAfterPartZeroOfAnAnsweredResync_RepeatsTheRequest()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        Assert.False(mirror.Apply(new ConstructState(Header, 1, 2, [Chunk(1)])));
        Assert.Single(requests);
        Assert.False(mirror.Apply(new ConstructState(Header, 0, 3, [Chunk(0)])));
        Assert.False(mirror.Apply(new ConstructState(Header, 2, 3, [Chunk(2)])));
        Assert.Equal(2, requests.Count);
    }

    [Fact]
    public void ConstructMirror_OperationApplied_ShipMode_RefreshesHeader()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        var requests = new List<EntityId>();
        mirror.ResyncNeeded += requests.Add;
        var changed = new List<EntityId>();
        mirror.Changed += changed.Add;
        Assert.True(mirror.Apply(new ConstructState(Header, 0, 1, [])));
        Assert.True(mirror.TryGetConstruct(Header.Id, out var value));
        var origin = new Vector3(4, 5, 6);
        var toShip = new ShipModeSetOp(Header.Id, ConstructMode.Ship, Header.Parent, 0, origin, Quaternion.Identity);
        Assert.True(mirror.Apply(new OperationApplied(new OpId(60), default, toShip, [new(Header.Id, 2, false)])));
        Assert.Equal(ConstructMode.Ship, value.Mode);
        Assert.Equal(origin, value.Origin);
        Assert.Equal(2u, value.Version);
        Assert.Contains(Header.Id, changed);

        // A stale parent counter does not match: the copy is left alone and repaired.
        var bad = new ShipModeSetOp(Header.Id, ConstructMode.Static, Header.Parent, 5, origin, Quaternion.Identity);
        Assert.False(mirror.Apply(new OperationApplied(new OpId(61), default, bad, [new(Header.Id, 3, false)])));
        Assert.Equal(ConstructMode.Ship, value.Mode);
        Assert.Single(requests);
    }
}

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
}

using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Replication;
using Horyzont.Core.World.Constructs;

namespace Horyzont.Core.Tests;

public sealed class ConstructReplicaTests
{
    [Fact]
    public void Create_CopiesChunksAndUnknownTypes_PreservesVersion()
    {
        var header = new ConstructHeaderRecord(new EntityId(5), FrameRef.Of(new PlanetId(1)), 0,
            ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 17);
        var blocks = new uint[4096];
        blocks[0] = BlockState.Create(2047, 0).Packed;
        var replica = ConstructReplica.Create(header, [new(new(header.Id, default), blocks)]);
        blocks[0] = 0;
        Assert.Equal(2047, replica.Value.GetBlock(default).TypeId);
        Assert.Equal(17u, replica.Value.Version);
        Assert.False(replica.TrySet(new CellPos(1, 0, 0), BlockState.Create(1, 0), 19));
        Assert.True(replica.Value.GetBlock(new CellPos(1, 0, 0)).IsEmpty);
        Assert.True(replica.TrySet(new CellPos(1, 0, 0), BlockState.Create(1, 0), 18));
        Assert.Equal(18u, replica.Value.Version);
        Assert.True(replica.TryReparent(FrameRef.Of(new PlanetId(2)), 1, Vector3.One, Quaternion.Identity));
        Assert.False(replica.TryReparent(FrameRef.Of(new PlanetId(1)), 1, Vector3.Zero, Quaternion.Identity));
        Assert.Equal(18u, replica.Value.Version);
    }

    [Fact]
    public void Create_DuplicateOrMalformedChunk_RejectsData()
    {
        var header = new ConstructHeaderRecord(new EntityId(5), FrameRef.Of(new PlanetId(1)), 0,
            ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 1);
        var chunk = new ConstructChunkRecord(new(header.Id, default), new uint[4096]);
        Assert.Throws<InvalidDataException>(() => ConstructReplica.Create(header, [chunk, chunk]));
        chunk.Blocks[0] = 0xF0000001;
        Assert.Throws<InvalidDataException>(() => ConstructReplica.Create(header, [chunk]));
    }
}

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

    [Fact]
    public void ConstructReplica_ShipModeSetOp_UpdatesModeAndPose()
    {
        var parent = FrameRef.Of(new PlanetId(1));
        var header = new ConstructHeaderRecord(new EntityId(5), parent, 3, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, 7);
        var replica = ConstructReplica.Create(header, []);
        var origin = new Vector3(1, 2, 3);
        Assert.False(replica.TrySetMode(ConstructMode.Ship, parent, 3, origin, Quaternion.Identity, 9));
        Assert.False(replica.TrySetMode(ConstructMode.Ship, parent, 2, origin, Quaternion.Identity, 8));
        Assert.False(replica.TrySetMode(ConstructMode.Static, parent, 3, origin, Quaternion.Identity, 8));
        Assert.False(replica.TrySetMode(ConstructMode.Ship, parent, 3, new Vector3(float.NaN, 0, 0), Quaternion.Identity, 8));
        Assert.Equal(ConstructMode.Static, replica.Value.Mode);
        Assert.Equal(7u, replica.Value.Version);
        Assert.True(replica.TrySetMode(ConstructMode.Ship, parent, 3, origin, Quaternion.Identity, 8));
        Assert.Equal(ConstructMode.Ship, replica.Value.Mode);
        Assert.Equal(origin, replica.Value.Origin);
        Assert.Equal(8u, replica.Value.Version);
    }
}

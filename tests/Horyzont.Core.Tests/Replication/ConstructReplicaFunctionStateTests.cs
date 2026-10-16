using System.Numerics;

using Horyzont.Core.Checkpoints;
using Horyzont.Core.Ids;
using Horyzont.Core.Replication;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Core.Tests.Replication;

/// <summary>K6-30, D14: the client copy of a construct follows the function state operation (49) by version, like the other mutations.</summary>
public sealed class ConstructReplicaFunctionStateTests
{
    private const ushort DoorType = FunctionalBlocks.DoorType;
    private static readonly CellPos _door = new(3, 1, 0);

    [Fact]
    public void TrySetFunctionState_NextVersion_Applies()
    {
        var replica = Replica(version: 7);
        var before = replica.Value.GetBlock(_door);

        Assert.True(replica.TrySetFunctionState(_door, DoorType, 1, 8));

        Assert.Equal(1, replica.Value.GetBlock(_door).FunctionState);
        Assert.Equal(before.WithFunctionState(1), replica.Value.GetBlock(_door));
        Assert.Equal(8u, replica.Value.Version);
        Assert.True(replica.TrySetFunctionState(_door, DoorType, 0, 9));
        Assert.Equal(before, replica.Value.GetBlock(_door));
        Assert.Equal(9u, replica.Value.Version);
    }

    [Fact]
    public void TrySetFunctionState_StaleVersion_Ignored()
    {
        var replica = Replica(version: 7);
        Assert.True(replica.TrySetFunctionState(_door, DoorType, 1, 8));
        var block = replica.Value.GetBlock(_door);

        // The same version again, an older one, a gap, a wrong type, an empty cell and a state above 7 leave the copy as it is.
        Assert.False(replica.TrySetFunctionState(_door, DoorType, 0, 8));
        Assert.False(replica.TrySetFunctionState(_door, DoorType, 0, 7));
        Assert.False(replica.TrySetFunctionState(_door, DoorType, 0, 10));
        Assert.False(replica.TrySetFunctionState(_door, FunctionalBlocks.LampType, 0, 9));
        Assert.False(replica.TrySetFunctionState(new CellPos(4, 1, 0), DoorType, 0, 9));
        Assert.False(replica.TrySetFunctionState(_door, DoorType, 8, 9));

        Assert.Equal(block, replica.Value.GetBlock(_door));
        Assert.Equal(8u, replica.Value.Version);
    }

    private static ConstructReplica Replica(uint version)
    {
        var header = new ConstructHeaderRecord(
            new EntityId(5), FrameRef.Of(new PlanetId(1)), 0, ConstructMode.Static, Vector3.Zero, Quaternion.Identity, version);
        var blocks = new uint[ConstructChunk.CellCount];
        blocks[_door.LocalIndex] = BlockState.Create(DoorType, 0).Packed;
        return ConstructReplica.Create(header, [new ConstructChunkRecord(new ConstructChunkKey(header.Id, _door.Chunk), blocks)]);
    }
}

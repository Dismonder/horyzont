using System.Numerics;

using Horyzont.Client.Replication;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Client.Tests.Replication;

public sealed class ConstructMirrorFunctionStateTests
{
    private const ushort DoorType = 5;
    private static readonly EntityId _id = new(5);
    private static readonly CellPos _low = new(0, 0, 0);
    private static readonly CellPos _high = new(0, 1, 0);

    /// <summary>A mirror with a construct of two door blocks at version 3.</summary>
    private static (ConstructMirror Mirror, List<EntityId> Resyncs, List<Operation> Applied) Rig()
    {
        var mirror = new ConstructMirror(new FakeTimeProvider());
        var resyncs = new List<EntityId>();
        var applied = new List<Operation>();
        mirror.ResyncNeeded += resyncs.Add;
        mirror.EditApplied += (op, _) => applied.Add(op);
        var door = BlockState.Create(DoorType, 0);
        Assert.True(mirror.Apply(Applied(1, new ConstructCreateOp(_id, FrameRef.Of(new PlanetId(1)), Vector3.Zero, Quaternion.Identity, _low, door), 1)));
        Assert.True(mirror.Apply(Applied(2, new BlockSetOp(_id, _high, door), 2)));
        return (mirror, resyncs, applied);
    }

    private static OperationApplied Applied(ulong op, Operation operation, uint version) =>
        new(new OpId(op), default, operation, [new ConstructChange(_id, version, false)]);

    [Fact]
    public void FunctionStateOp_AppliesToReplica()
    {
        var (mirror, resyncs, applied) = Rig();

        Assert.True(mirror.Apply(Applied(3, new BlockFunctionStateSetOp(_id, _low, DoorType, 1), 3)));

        Assert.True(mirror.TryGetConstruct(_id, out var construct));
        Assert.Equal(1, construct.GetBlock(_low).FunctionState);
        Assert.Equal(0, construct.GetBlock(_high).FunctionState);
        Assert.Equal(3u, construct.Version);
        Assert.Empty(resyncs);
        Assert.IsType<BlockFunctionStateSetOp>(applied[^1]);
    }

    [Fact]
    public void DoorColumnCompound_Applies()
    {
        var (mirror, resyncs, applied) = Rig();
        var column = new CompoundOp([new BlockFunctionStateSetOp(_id, _low, DoorType, 1), new BlockFunctionStateSetOp(_id, _high, DoorType, 1)]);

        Assert.True(mirror.Apply(Applied(3, column, 3)));

        Assert.True(mirror.TryGetConstruct(_id, out var construct));
        Assert.Equal(1, construct.GetBlock(_low).FunctionState);
        Assert.Equal(1, construct.GetBlock(_high).FunctionState);
        Assert.Equal(3u, construct.Version); // one bump for the whole column
        Assert.Empty(resyncs);
        Assert.IsType<CompoundOp>(applied[^1]);
    }

    [Fact]
    public void FunctionStateGap_RequestsResync()
    {
        var (mirror, resyncs, _) = Rig();

        Assert.False(mirror.Apply(Applied(4, new BlockFunctionStateSetOp(_id, _low, DoorType, 1), 4)));

        Assert.Equal([_id], resyncs);
        Assert.True(mirror.TryGetConstruct(_id, out var construct));
        Assert.Equal(0, construct.GetBlock(_low).FunctionState);
    }

    [Fact]
    public void FunctionStateOfAnotherBlockType_RequestsResync()
    {
        var (mirror, resyncs, _) = Rig();

        Assert.False(mirror.Apply(Applied(3, new BlockFunctionStateSetOp(_id, _low, 9, 1), 3)));

        Assert.Equal([_id], resyncs);
    }

    [Fact]
    public void DoorColumnWithOneWrongCell_ChangesNothingAndRequestsResync()
    {
        var (mirror, resyncs, _) = Rig();
        var column = new CompoundOp([new BlockFunctionStateSetOp(_id, _low, DoorType, 1), new BlockFunctionStateSetOp(_id, _high, 9, 1)]);

        Assert.False(mirror.Apply(Applied(3, column, 3)));

        Assert.Equal([_id], resyncs);
        Assert.True(mirror.TryGetConstruct(_id, out var construct));
        Assert.Equal(0, construct.GetBlock(_low).FunctionState);
    }

    [Fact]
    public void PlaceChestCompoundWithItemParts_AppliesTheBlockPart()
    {
        // The server sends the projection (only the block part), but a whole compound must work as well (D14).
        var (mirror, resyncs, _) = Rig();
        var chest = BlockState.Create(2, 0);
        var compound = new CompoundOp([
            new BlockEntitySetOp(_id, new CellPos(1, 0, 0), 2, new Horyzont.Core.BlockEntities.ChestBlockState(new ContainerId(7))),
            new BlockSetOp(_id, new CellPos(1, 0, 0), chest)]);

        Assert.True(mirror.Apply(Applied(3, compound, 3)));

        Assert.Empty(resyncs);
        Assert.True(mirror.TryGetConstruct(_id, out var construct));
        Assert.Equal(2, construct.GetBlock(new CellPos(1, 0, 0)).TypeId);
    }
}

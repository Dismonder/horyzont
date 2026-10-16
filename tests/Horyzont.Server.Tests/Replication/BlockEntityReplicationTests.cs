using System.Numerics;

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Core.World.Constructs;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Replication;

/// <summary>
/// K6-30 D14 and D15: the states of the blocks of a construct (message 46) follow the state of the construct to every recipient with a live
/// link, and the operations they travel in reach other clients as their construct part only.
/// </summary>
public sealed class BlockEntityReplicationTests
{
    private static WorldState World() => WorldState.CreateNew(
        WorldFactory.DefaultWorldId, 42, [WorldFactory.HomePlanetParams], FunctionalBlocks.BlockKeys, ["stone", "dirt"], SurvivalContentFactory.ItemKeys);

    /// <summary>A construct with a stone floor, a chest block, a bed block and a two-block door, without any state or container.</summary>
    private static EntityId AddBlocks(ReplicationRig rig)
    {
        var id = rig.Runtime.State.EntityIds.PeekNext();
        var sim = rig.Runtime.Simulation;
        sim.ApplyServerOperation(new ConstructCreateOp(
            id, FrameRef.Of(WorldFactory.HomePlanet), new Vector3(0, 4000, 0), Quaternion.Identity, new CellPos(0, 0, 0),
            BlockState.Create(FunctionalBlocks.StoneType, 0)));
        sim.ApplyServerOperation(new BlockSetOp(id, FunctionalBlocks.ChestCell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        sim.ApplyServerOperation(new BlockSetOp(id, FunctionalBlocks.BedCell, BlockState.Create(FunctionalBlocks.BedType, 0)));
        sim.ApplyServerOperation(new BlockSetOp(id, FunctionalBlocks.DoorLowCell, BlockState.Create(FunctionalBlocks.DoorType, 0)));
        sim.ApplyServerOperation(new BlockSetOp(id, FunctionalBlocks.DoorHighCell, BlockState.Create(FunctionalBlocks.DoorType, 0)));
        return id;
    }

    private static ContainerId AddChestState(ReplicationRig rig, EntityId construct, PlayerId owner)
    {
        var sim = rig.Runtime.Simulation;
        var container = rig.Runtime.State.ContainerIds.PeekNext();
        sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, FunctionalBlocks.ChestCell), owner, ContainerAccess.Shared, 8),
            new BlockEntitySetOp(construct, FunctionalBlocks.ChestCell, FunctionalBlocks.ChestType, new ChestBlockState(container)),
        ]));
        return container;
    }

    private static BlockEntitySetOp BedState(EntityId construct, PlayerId owner) =>
        new(construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType, new BedBlockState(owner));

    private static List<BlockEntityStates> StatesOf(RigClient client, EntityId construct) =>
        client.Received.OfType<BlockEntityStates>().Where(m => m.Construct == construct).ToList();

    private static int LastIndexOf<T>(RigClient client, Func<T, bool> match)
        where T : Message
    {
        for (var i = client.Received.Count - 1; i >= 0; i--)
        {
            if (client.Received[i] is T message && match(message))
            {
                return i;
            }
        }

        return -1;
    }

    [Fact]
    public async Task LinkLive_SendsStatesAfterConstructState()
    {
        await using var rig = await ReplicationRig.StartAsync(World);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(2000f, 4000f, 0f));
        var construct = AddBlocks(rig);
        var chest = AddChestState(rig, construct, alice.Player);
        rig.Runtime.Simulation.ApplyServerOperation(BedState(construct, alice.Player));
        rig.Step(10);
        Assert.Empty(StatesOf(alice, construct));

        // Alice walks into range: the full state of the construct comes first, then the states of its blocks.
        rig.Place(alice.Player, new Vector3(5f, 4000f, 0f));
        rig.Step(60);

        var states = Assert.Single(StatesOf(alice, construct));
        Assert.Equal(
            [new BlockEntityEntry(FunctionalBlocks.ChestCell, new ChestBlockState(chest)), new BlockEntityEntry(FunctionalBlocks.BedCell, new BedBlockState(alice.Player))],
            states.Entries);
        var lastPart = LastIndexOf<ConstructState>(alice, m => m.Header.Id == construct);
        Assert.True(lastPart >= 0);
        Assert.True(alice.Received.IndexOf(states) > lastPart, "the block states have to follow the last part of the construct state");
    }

    [Fact]
    public async Task BlockEntitySet_SendsStatesToLiveRecipientsOnly()
    {
        await using var rig = await ReplicationRig.StartAsync(World);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(5f, 4000f, 0f));
        var bob = await rig.JoinAsync("bob", 2, new Vector3(2000f, 4000f, 0f));
        var construct = AddBlocks(rig);
        rig.Step(60);
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, construct));
        Assert.False(rig.Replication.Knows(bob.Player, construct));
        alice.Received.Clear();
        bob.Received.Clear();

        rig.Runtime.Simulation.ApplyServerOperation(BedState(construct, bob.Player));
        rig.Step(3);

        var states = Assert.Single(StatesOf(alice, construct));
        Assert.Equal(new BlockEntityEntry(FunctionalBlocks.BedCell, new BedBlockState(bob.Player)), Assert.Single(states.Entries));
        Assert.Empty(StatesOf(bob, construct));
        Assert.Empty(bob.Received.OfType<OperationApplied>());
    }

    [Fact]
    public async Task TransferringRecipient_GetsStatesOnLinkLive()
    {
        var gate = new GatedSendBudget { Open = false };
        await using var rig = await ReplicationRig.StartAsync(World, sendBudget: gate);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(5f, 4000f, 0f));
        var construct = AddBlocks(rig);
        rig.Step(30);
        Assert.True(rig.Replication.Constructs.IsTransferring(alice.Player, construct));

        // The state of a block changes while the construct state is still on its way: nothing is sent before the link is live.
        rig.Runtime.Simulation.ApplyServerOperation(BedState(construct, alice.Player));
        rig.Step(10);
        Assert.Empty(StatesOf(alice, construct));

        gate.Open = true;
        rig.Step(60);

        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, construct));
        var states = Assert.Single(StatesOf(alice, construct));
        Assert.Equal(new BlockEntityEntry(FunctionalBlocks.BedCell, new BedBlockState(alice.Player)), Assert.Single(states.Entries));
        Assert.True(alice.Received.IndexOf(states) > LastIndexOf<ConstructState>(alice, m => m.Header.Id == construct));
    }

    [Fact]
    public async Task PlaceChest_OperationAppliedCarriesBlockSetOnly()
    {
        await using var rig = await ReplicationRig.StartAsync(World);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(5f, 4000f, 0f));
        var construct = AddBlocks(rig);
        rig.Step(60);
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, construct));
        alice.Received.Clear();

        var container = rig.Runtime.State.ContainerIds.PeekNext();
        var cell = FunctionalBlocks.FreeCell;
        rig.Runtime.Simulation.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), alice.Player, ContainerAccess.Shared, 8),
            new BlockEntitySetOp(construct, cell, FunctionalBlocks.ChestType, new ChestBlockState(container)),
            new BlockSetOp(construct, cell, BlockState.Create(FunctionalBlocks.ChestType, 0)),
        ]));
        rig.Step(3);

        var applied = Assert.Single(alice.Received.OfType<OperationApplied>());
        var set = Assert.IsType<BlockSetOp>(applied.Operation);
        Assert.Equal((construct, cell), (set.Construct, set.Cell));
        Assert.Empty(alice.Received.OfType<ConstructState>());
        var states = Assert.Single(StatesOf(alice, construct));
        Assert.Contains(new BlockEntityEntry(cell, new ChestBlockState(container)), states.Entries);
        Assert.True(alice.Received.IndexOf(states) > alice.Received.IndexOf(applied), "the block states follow the operation of the same change");
        Assert.DoesNotContain(alice.Received, m => m is ContainerState or ContainerDelta);
    }

    [Fact]
    public async Task DoorToggle_OperationAppliedForwarded_NoResync()
    {
        await using var rig = await ReplicationRig.StartAsync(World);
        var alice = await rig.JoinAsync("alice", 1, new Vector3(5f, 4000f, 0f));
        var construct = AddBlocks(rig);
        rig.Step(60);
        Assert.True(rig.Replication.Constructs.IsLive(alice.Player, construct));
        alice.Received.Clear();

        var toggle = new CompoundOp(
        [
            new BlockFunctionStateSetOp(construct, FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType, 1),
            new BlockFunctionStateSetOp(construct, FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType, 1),
        ]);
        rig.Runtime.Simulation.ApplyServerOperation(toggle);
        rig.Step(30);

        var applied = Assert.Single(alice.Received.OfType<OperationApplied>());
        Assert.Equal(toggle.Components, Assert.IsType<CompoundOp>(applied.Operation).Components);
        Assert.Empty(alice.Received.OfType<ConstructState>());
        Assert.DoesNotContain(alice.Received, m => m is EntitySpawn or EntityDespawn or ConstructRemoved);
        Assert.Empty(StatesOf(alice, construct));
    }
}

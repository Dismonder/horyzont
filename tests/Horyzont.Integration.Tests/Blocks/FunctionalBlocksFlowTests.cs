using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence.Faults;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Blocks;

/// <summary>
/// Client, in-memory transport, session, simulation, replication and writer: chests, beds, doors and lamps from the first intent to a
/// restart (K6-30 stage C, the criterion of the spec: they work and they survive a restart).
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class FunctionalBlocksFlowTests
{
    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly CellPos _chestCell = new(1, 1, 0);

    private static ItemMoveIntent Move(ContainerId from, ushort slot, ItemMoveTarget to, ushort count, ItemTypeId type, ushort have) =>
        new(new SlotRef(from, slot), to, count, type, have);

    private static PlaceBlockIntent PlaceChest(EntityId construct) =>
        new(new ConstructFaceTarget(construct, new CellPos(1, 0, 0), Face.PosY), FunctionalBlocks.ChestType, 0);

    private static BlockInteractIntent Interact(EntityId construct, CellPos cell, BlockInteractAction action) => new(construct, cell, action);

    private static async Task DisconnectAllAsync(FunctionalBlocksRig rig)
    {
        foreach (var player in rig.Players)
        {
            player.Client.Disconnect();
        }

        Assert.True(await rig.RunUntilAsync(() => !rig.Runtime.State.Players.Online.Any()));
    }

    /// <summary>A chest block with a shared container (hosted by the block) and its state, made by server operations.</summary>
    private static ContainerId SetUpChest(FunctionalBlocksRig rig, EntityId construct, CellPos cell, ushort size = FunctionalBlocksRig.ChestSlots)
    {
        var sim = rig.Runtime.Simulation;
        sim.ApplyServerOperation(new BlockSetOp(construct, cell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        var container = rig.Runtime.State.ContainerIds.PeekNext();
        sim.ApplyServerOperation(new ContainerCreateOp(
            container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), PlayerId.None, ContainerAccess.Shared, size));
        sim.ApplyServerOperation(new BlockEntitySetOp(construct, cell, FunctionalBlocks.ChestType, new ChestBlockState(container)));
        return container;
    }

    private static async Task<ContainerId> OpenChestAsync(FunctionalBlocksRig rig, RigPlayer player, EntityId construct, CellPos cell)
    {
        Assert.True(
            await rig.RunUntilAsync(() => player.StatesOf(construct)?.Entries.Any(e => e.Cell == cell) == true), "the block states did not arrive");
        var chest = ((ChestBlockState)player.StatesOf(construct)!.Entries.Single(e => e.Cell == cell).State).Container;
        player.Client.Send(new ContainerSubscribe(chest));
        Assert.True(await rig.RunUntilAsync(() => player.Mirror.Ids.Contains(chest)), "the chest state did not arrive");
        return chest;
    }

    [Fact]
    public async Task PlaceChest_MoveItems_Restart_ItemsAndChestIdentical()
    {
        using var dir = new TempDir();
        ulong hash;
        EntityId construct;
        ContainerId chest;
        ItemStack[] chestSlots;
        await using (var first = new FunctionalBlocksRig(dir.Path))
        {
            await first.StartAsync();
            construct = first.BuildConstruct();
            var alice = await first.JoinAsync(1);
            var hotbar = alice.Mirror.IdOf(ContainerKind.Hotbar);

            Assert.Equal(RejectReason.None, (await first.SubmitAsync(alice, PlaceChest(construct))).Reason);
            chest = await OpenChestAsync(first, alice, construct, _chestCell);
            Assert.Equal(FunctionalBlocksRig.ChestSlots, alice.Mirror.SlotsOf(chest).Length);

            var moved = await first.SubmitAsync(alice, Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(chest, 2)), 2, _gem, 3));
            Assert.Equal(RejectReason.None, moved.Reason);
            Assert.Equal(new ItemStack(_gem, 2), alice.Mirror.SlotsOf(chest)[2]);
            Assert.Equal(new ItemStack(_gem, 1), alice.Mirror.Slot(ContainerKind.Hotbar, 1));

            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
            Assert.True(first.Runtime.State.Containers.TryGet(chest, out var live));
            chestSlots = live.Slots.ToArray();
        }

        Assert.Equal(new ItemStack(_gem, 2), chestSlots[2]);

        await using var second = new FunctionalBlocksRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.True(second.Runtime.State.Containers.TryGet(chest, out var restored));
        Assert.Equal(ContainerKind.Chest, restored.Kind);
        Assert.Equal(chestSlots, restored.Slots.ToArray());
        Assert.True(second.Runtime.State.BlockEntities.TryGet(new BlockRef(construct, _chestCell), out var state));
        Assert.Equal(new ChestBlockState(chest), state);
        Assert.Equal(FunctionalBlocks.ChestType, second.Construct.GetBlock(_chestCell).TypeId);
        await second.StopAsync();
    }

    [Fact]
    public async Task BedSleep_Restart_RespawnPointKept()
    {
        using var dir = new TempDir();
        ulong hash;
        EntityId construct;
        PlayerId player;
        await using (var first = new FunctionalBlocksRig(dir.Path))
        {
            await first.StartAsync();
            construct = first.BuildConstruct();
            var alice = await first.JoinAsync(1);
            player = alice.Player;

            // The bed is put up by an intent, so it has its state (a plain bed block has none and cannot be slept in).
            var placed = await first.SubmitAsync(
                alice, new PlaceBlockIntent(new ConstructFaceTarget(construct, new CellPos(1, 0, 0), Face.PosY), FunctionalBlocks.BedType, 0));
            Assert.Equal(RejectReason.None, placed.Reason);
            Assert.Equal(FunctionalBlocks.BedCell, new CellPos(1, 1, 0));

            var slept = await first.SubmitAsync(alice, Interact(construct, FunctionalBlocks.BedCell, BlockInteractAction.Sleep));

            Assert.Equal(RejectReason.None, slept.Reason);
            Assert.True(await first.RunUntilAsync(() => alice.StatesOf(construct) is not null));
            Assert.Equal(new BedBlockState(player), alice.StatesOf(construct)!.Entries.Single().State);
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
        }

        await using var second = new FunctionalBlocksRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.True(second.Runtime.State.Progress.TryGet(player, out var progress));
        Assert.Equal(new Core.Progress.BedRef(construct, FunctionalBlocks.BedCell, FunctionalBlocks.BedType), progress.RespawnBed);
        Assert.True(second.Runtime.State.BlockEntities.TryGet(new BlockRef(construct, FunctionalBlocks.BedCell), out var state));
        Assert.Equal(new BedBlockState(player), state);
        await second.StopAsync();
    }

    [Fact]
    public async Task DoorAndLamp_Toggle_Restart_StateKept()
    {
        using var dir = new TempDir();
        ulong hash;
        await using (var first = new FunctionalBlocksRig(dir.Path))
        {
            await first.StartAsync();
            var construct = first.BuildConstruct(
                (FunctionalBlocks.LampCell, FunctionalBlocks.LampType), (FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType),
                (FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType));
            var alice = await first.JoinAsync(1);
            var version = first.Construct.Version;

            Assert.Equal(RejectReason.None, (await first.SubmitAsync(alice, Interact(construct, FunctionalBlocks.DoorLowCell, BlockInteractAction.Toggle))).Reason);
            Assert.Equal(RejectReason.None, (await first.SubmitAsync(alice, Interact(construct, FunctionalBlocks.LampCell, BlockInteractAction.Toggle))).Reason);

            Assert.Equal(version + 2, first.Construct.Version);
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
        }

        await using var second = new FunctionalBlocksRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        var restored = second.Construct;
        Assert.Equal([1, 1, 1], new[]
        {
            restored.GetBlock(FunctionalBlocks.DoorLowCell).FunctionState,
            restored.GetBlock(FunctionalBlocks.DoorHighCell).FunctionState,
            restored.GetBlock(FunctionalBlocks.LampCell).FunctionState,
        }.Select(s => (int)s));
        await second.StopAsync();
    }

    [Fact]
    public async Task TwoClients_DoorToggle_BothSeeOpen()
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path);
        await rig.StartAsync();
        var construct = rig.BuildConstruct((FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorType), (FunctionalBlocks.DoorHighCell, FunctionalBlocks.DoorType));
        var alice = await rig.JoinAsync(1);
        var bob = await rig.JoinAsync(2);

        Assert.Equal(RejectReason.None, (await rig.SubmitAsync(alice, Interact(construct, FunctionalBlocks.DoorLowCell, BlockInteractAction.Toggle))).Reason);

        foreach (var player in new[] { alice, bob })
        {
            Assert.True(await rig.RunUntilAsync(() => player.Applied.Any(IsDoorOpen)), "a client did not see the door open");
            var applied = Assert.Single(player.Applied, IsDoorOpen);
            Assert.Equal(
                [FunctionalBlocks.DoorLowCell, FunctionalBlocks.DoorHighCell],
                ((CompoundOp)applied.Operation).Components.Cast<BlockFunctionStateSetOp>().Select(c => c.Cell));
        }

        static bool IsDoorOpen(OperationApplied m) =>
            m.Operation is CompoundOp { Components: [BlockFunctionStateSetOp { State: 1 }, BlockFunctionStateSetOp { State: 1 }] };
    }

    [Fact]
    public async Task TwoClientsSameChest_RandomMoves_NoDuplication()
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path);
        await rig.StartAsync();
        var alice = await rig.JoinAsync(1, expectConstruct: false);
        var bob = await rig.JoinAsync(2, expectConstruct: false);
        var construct = rig.BuildConstruct();
        var chest = SetUpChest(rig, construct, _chestCell);
        await OpenChestAsync(rig, alice, construct, _chestCell);
        await OpenChestAsync(rig, bob, construct, _chestCell);
        var before = TotalsByType(rig);
        Assert.Equal(6, before[_gem]);

        var random = new Random(30);
        var replies = new Dictionary<RejectReason, int>();
        for (var round = 0; round < 500; round++)
        {
            foreach (var player in new[] { alice, bob })
            {
                var intent = RandomMove(random, player, chest);
                if (intent is not null)
                {
                    player.Client.SubmitIntent(intent);
                }
            }

            rig.StepMany(3);
        }

        rig.StepMany(30);
        foreach (var reply in rig.Players.SelectMany(p => p.Hooks.Replies))
        {
            replies[reply.Reason] = replies.GetValueOrDefault(reply.Reason) + 1;
        }

        Assert.True(replies.GetValueOrDefault(RejectReason.None) > 200, "too few accepted moves: " + string.Join(", ", replies));
        Assert.Equal(before, TotalsByType(rig));
        Assert.True(rig.Runtime.State.Containers.TryGet(chest, out var live));
        foreach (var container in rig.Runtime.State.Containers.All)
        {
            foreach (var player in new[] { alice, bob })
            {
                if (player.Mirror.Ids.Contains(container.Id))
                {
                    Assert.Equal(container.Slots.ToArray(), player.Mirror.SlotsOf(container.Id));
                    Assert.Equal(container.Version, player.Mirror.VersionOf(container.Id));
                }
            }
        }

        Assert.Equal(live.Slots.ToArray(), alice.Mirror.SlotsOf(chest));
        Assert.Equal(live.Slots.ToArray(), bob.Mirror.SlotsOf(chest));
    }

    [Theory]
    [InlineData(FaultPoint.BeforeJournalCommit, false)]
    [InlineData(FaultPoint.AfterJournalCommit, true)]
    public async Task CrashMidChestPlacement_WholeOrNothing(FaultPoint point, bool chestExists)
    {
        using var dir = new TempDir();
        var faults = new ArmedFaults();
        EntityId construct;
        await using (var crashed = new FunctionalBlocksRig(dir.Path, faults))
        {
            await crashed.StartAsync();
            construct = crashed.BuildConstruct();
            var alice = await crashed.JoinAsync(1);

            // Everything before the placement is durable (its rows are in the file), then the process dies while the placement is written.
            Assert.True(await crashed.RunUntilAsync(() => RawJournalRows(dir.Path) >= (long)crashed.Runtime.State.LastOpId.Value));
            faults.Arm(point);
            Assert.Equal(RejectReason.None, (await crashed.SubmitAsync(alice, PlaceChest(construct))).Reason);
            Assert.True(await crashed.RunUntilAsync(() => faults.Crashed));
            crashed.Runtime.RequestShutdown();
            for (var i = 0; i < 8000 && !crashed.Runtime.IsStopped; i++)
            {
                crashed.Step();
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        await using var restarted = new FunctionalBlocksRig(dir.Path);
        await restarted.StartAsync();

        var state = restarted.Runtime.State;
        Assert.Equal(chestExists ? FunctionalBlocks.ChestType : (ushort)0, restarted.Construct.GetBlock(_chestCell).TypeId);
        Assert.Equal(chestExists ? 1 : 0, state.BlockEntities.Count);
        Assert.Equal(chestExists ? 4 : 3, state.Containers.Count);
        Assert.Equal(chestExists ? 1 : 0, state.Containers.OfKind(ContainerKind.Chest).Count);
        await restarted.StopAsync();
    }

    [Fact]
    public async Task QuickStack_EndToEnd_DeltasToSubscribers()
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path);
        await rig.StartAsync();
        var alice = await rig.JoinAsync(1, expectConstruct: false);
        var bob = await rig.JoinAsync(2, expectConstruct: false);
        var construct = rig.BuildConstruct();
        var chest = SetUpChest(rig, construct, _chestCell);
        rig.Runtime.Simulation.ApplyServerOperation(new ItemCreateOp(new SlotRef(chest, 0), _gem, 4, 5, ItemSource.Admin));
        await OpenChestAsync(rig, alice, construct, _chestCell);
        await OpenChestAsync(rig, bob, construct, _chestCell);
        var hotbar = alice.Mirror.IdOf(ContainerKind.Hotbar);
        var bag = alice.Mirror.IdOf(ContainerKind.PlayerBag);
        Assert.Equal(RejectReason.None, (await rig.SubmitAsync(alice, Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(bag, 0)), 3, _gem, 3))).Reason);
        var bobDeltas = bob.Hooks.Messages.OfType<ContainerDelta>().Count(d => d.Container == chest);

        var reply = await rig.SubmitAsync(alice, new QuickStackIntent());

        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(alice.Mirror.SlotsOf(bag)[0].IsEmpty);
        Assert.Equal(new ItemStack(_gem, 5), alice.Mirror.SlotsOf(chest)[0]);
        Assert.Equal(new ItemStack(_gem, 2), alice.Mirror.SlotsOf(chest)[1]);
        Assert.True(bob.Hooks.Messages.OfType<ContainerDelta>().Count(d => d.Container == chest) > bobDeltas);
        Assert.Equal(alice.Mirror.SlotsOf(chest), bob.Mirror.SlotsOf(chest));
        Assert.True(rig.Runtime.State.Containers.TryGet(chest, out var live));
        Assert.Equal(live.Slots.ToArray(), bob.Mirror.SlotsOf(chest));
    }

    /// <summary>One random move of the client between its bag, its hotbar and the chest, judged on what its mirror knows.</summary>
    private static ItemMoveIntent? RandomMove(Random random, RigPlayer player, ContainerId chest)
    {
        var own = new[] { player.Mirror.IdOf(ContainerKind.PlayerBag), player.Mirror.IdOf(ContainerKind.Hotbar) };
        var from = random.Next(3) == 0 ? chest : own[random.Next(own.Length)];
        var slots = player.Mirror.SlotsOf(from);
        var filled = Enumerable.Range(0, slots.Length).Where(i => !slots[i].IsEmpty).ToArray();
        if (filled.Length == 0)
        {
            return null;
        }

        var slot = filled[random.Next(filled.Length)];
        var stack = slots[slot];
        var target = from == chest ? own[random.Next(own.Length)] : chest;
        return new ItemMoveIntent(
            new SlotRef(from, (ushort)slot), ItemMoveTarget.AnyIn(target), (ushort)(1 + random.Next(stack.Count)), stack.Type, stack.Count);
    }

    private static SortedDictionary<ItemTypeId, long> TotalsByType(FunctionalBlocksRig rig)
    {
        var totals = new SortedDictionary<ItemTypeId, long>(Comparer<ItemTypeId>.Create((a, b) => a.Value.CompareTo(b.Value)));
        foreach (var container in rig.Runtime.State.Containers.All)
        {
            foreach (var stack in container.Slots)
            {
                if (!stack.IsEmpty)
                {
                    totals[stack.Type] = totals.GetValueOrDefault(stack.Type) + stack.Count;
                }
            }
        }

        return totals;
    }

    private static long RawJournalRows(string directory)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = FunctionalBlocksRig.WorldPath(directory),
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                Pooling = false,
            }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(op_id), 0) FROM journal";
        return (long)command.ExecuteScalar()!;
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "horyzont-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Safe to leave: the directory is unique and under the temp directory.
            }
        }
    }
}

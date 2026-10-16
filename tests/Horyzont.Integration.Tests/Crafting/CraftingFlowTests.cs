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

namespace Horyzont.Integration.Tests.Crafting;

/// <summary>
/// Client, in-memory transport, session, simulation, replication and writer: crafting from several chests and the furnace from the first intent to a
/// restart (K6-32 stage C; the criteria of the spec: crafting from five chests is atomic, the furnace works by ticks). The rig is
/// <see cref="FunctionalBlocksRig"/> with the crafting content, the station service and the progress replication.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class CraftingFlowTests
{
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);
    private static readonly ItemTypeId _ore = WorldFactory.Item(SurvivalContentFactory.OreKey);
    private static readonly ItemTypeId _peat = WorldFactory.Item(SurvivalContentFactory.PeatKey);
    private static readonly ItemTypeId _brick = WorldFactory.Item(SurvivalContentFactory.BrickKey);
    private static readonly ItemTypeId _ingot = WorldFactory.Item(SurvivalContentFactory.IngotKey);
    private static readonly CellPos _furnaceCell = FunctionalBlocks.FreeCell;

    private static async Task DisconnectAllAsync(FunctionalBlocksRig rig)
    {
        foreach (var player in rig.Players)
        {
            player.Client.Disconnect();
        }

        Assert.True(await rig.RunUntilAsync(() => !rig.Runtime.State.Players.Online.Any()));
    }

    /// <summary>A chest block with a shared container (hosted by the block), its state and the items from slot 0 on, made by server operations.</summary>
    private static ContainerId SetUpChest(FunctionalBlocksRig rig, EntityId construct, CellPos cell, params (ItemTypeId Type, ushort Count)[] items)
    {
        var sim = rig.Runtime.Simulation;
        sim.ApplyServerOperation(new BlockSetOp(construct, cell, BlockState.Create(FunctionalBlocks.ChestType, 0)));
        var container = rig.Runtime.State.ContainerIds.PeekNext();
        sim.ApplyServerOperation(new CompoundOp(
            [
            new ContainerCreateOp(
            container, ContainerKind.Chest, ContainerHost.ConstructBlock(construct, cell), PlayerId.None, ContainerAccess.Shared, FunctionalBlocksRig.ChestSlots),
            new BlockEntitySetOp(construct, cell, FunctionalBlocks.ChestType, new ChestBlockState(container)),
            ]));
        for (var i = 0; i < items.Length; i++)
        {
            sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(container, (ushort)i), items[i].Type, items[i].Count, 500, ItemSource.Admin));
        }

        return container;
    }

    /// <summary>A furnace as the resolver puts one up (D18), choosing <c>t:smelt</c> and started now, with ore and peat in its input.</summary>
    private static (ContainerId Input, ContainerId Output, Tick Started) SetUpFurnace(FunctionalBlocksRig rig, EntityId construct, ushort ore, ushort peat)
    {
        var sim = rig.Runtime.Simulation;
        var input = rig.Runtime.State.ContainerIds.PeekNext();
        var output = new ContainerId(input.Value + 1);
        var host = ContainerHost.ConstructBlock(construct, _furnaceCell);
        var started = rig.Runtime.State.CurrentTick;
        sim.ApplyServerOperation(new CompoundOp(
        [
            new ContainerCreateOp(input, ContainerKind.StationInput, host, PlayerId.None, ContainerAccess.Shared, CraftingBlocks.InputSlots),
            new ContainerCreateOp(output, ContainerKind.StationOutput, host, PlayerId.None, ContainerAccess.Shared, CraftingBlocks.OutputSlots),
            new BlockEntitySetOp(construct, _furnaceCell, CraftingBlocks.FurnaceType, new StationBlockState(input, output, "t:smelt", started)),
            new BlockSetOp(construct, _furnaceCell, BlockState.Create(CraftingBlocks.FurnaceType, 0)),
        ]));
        sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(input, 0), _ore, ore, 500, ItemSource.Admin));
        sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(input, 1), _peat, peat, 500, ItemSource.Admin));
        return (input, output, started);
    }

    private static int CountIn(FunctionalBlocksRig rig, ContainerId container, ItemTypeId type)
    {
        Assert.True(rig.Runtime.State.Containers.TryGet(container, out var live));
        return live.Slots.ToArray().Where(s => s.Type == type).Sum(s => (int)s.Count);
    }

    private static long Total(FunctionalBlocksRig rig, ItemTypeId type) =>
        rig.Runtime.State.Containers.All.Sum(c => c.Slots.ToArray().Where(s => s.Type == type).Sum(s => (long)s.Count));

    private static async Task OpenAsync(FunctionalBlocksRig rig, RigPlayer player, params ContainerId[] containers)
    {
        foreach (var container in containers)
        {
            player.Client.Send(new ContainerSubscribe(container));
            Assert.True(await rig.RunUntilAsync(() => player.Mirror.Ids.Contains(container)), "the container state did not arrive");
        }
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

    [Theory]
    [InlineData(FaultPoint.BeforeJournalCommit, false)]
    [InlineData(FaultPoint.AfterJournalCommit, true)]
    public async Task CraftFromFiveChests_CrashBetweenComponents_WholeOrNothing(FaultPoint point, bool crafted)
    {
        using var dir = new TempDir();
        var faults = new ArmedFaults();
        ContainerId[] chests;
        ContainerId bag;
        await using (var crashed = new FunctionalBlocksRig(dir.Path, faults, crafting: true))
        {
            await crashed.StartAsync();
            var construct = crashed.BuildConstruct();
            chests = [.. Enumerable.Range(0, 5).Select(i => SetUpChest(crashed, construct, new CellPos(i, 1, 0), (_stone, 2)))];
            var alice = await crashed.JoinAsync(1);
            bag = alice.Mirror.IdOf(ContainerKind.PlayerBag);
            crashed.Runtime.Simulation.ApplyServerOperation(new ItemDiscoverOp(alice.Player, _stone));

            // Everything before the craft is durable, then the process dies while the craft (ten stones from five chests, five bricks, the discovery) is written.
            Assert.True(await crashed.RunUntilAsync(() => RawJournalRows(dir.Path) >= (long)crashed.Runtime.State.LastOpId.Value));
            faults.Arm(point);
            Assert.Equal(RejectReason.None, (await crashed.SubmitAsync(alice, new CraftIntent("t:hand", 5, null))).Reason);
            Assert.True(await crashed.RunUntilAsync(() => faults.Crashed));
            crashed.Runtime.RequestShutdown();
            for (var i = 0; i < 8000 && !crashed.Runtime.IsStopped; i++)
            {
                crashed.Step();
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        await using var restarted = new FunctionalBlocksRig(dir.Path, crafting: true);
        await restarted.StartAsync();

        // The chests hold ten stones and the bag no brick, or no stone and five bricks: the compound was written whole or not at all.
        var stones = chests.Sum(c => CountIn(restarted, c, _stone));
        Assert.Equal(crafted ? 0 : 10, stones);
        Assert.Equal(crafted ? 5 : 0, CountIn(restarted, bag, _brick));
        Assert.Equal(crafted, restarted.Runtime.State.Progress.TryGet(new PlayerId(1), out var progress) && progress.DiscoveredItems.Contains(_brick));
        await restarted.StopAsync();
    }

    private sealed record FurnaceOutcome(int Ore, int Peat, int Ingots, long StartedAfterSetUp);

    private static async Task<FurnaceOutcome> RunFurnaceAsync(int openEvery)
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path, crafting: true);
        await rig.StartAsync();
        var construct = rig.BuildConstruct();
        var alice = await rig.JoinAsync(1);

        // Sixteen ore and eight peat are eight pieces; about 326 ticks pass, which is five pieces and a bit (a margin of 26 ticks either way).
        var (input, output, started) = SetUpFurnace(rig, construct, ore: 16, peat: 8);
        for (var frame = 0; frame < 340; frame++)
        {
            if (openEvery > 0 && frame % openEvery == 0)
            {
                alice.Client.Send(new ContainerSubscribe(input));
                alice.Client.Send(new ContainerSubscribe(output));
            }

            rig.Step();
            if (openEvery > 0 && frame % openEvery == 3)
            {
                alice.Client.Send(new ContainerUnsubscribe(input));
                alice.Client.Send(new ContainerUnsubscribe(output));
            }
        }

        // The first time (for the quiet furnace) or the last time of all: the furnace is opened and settled.
        await OpenAsync(rig, alice, input, output);
        rig.StepMany(3);

        Assert.True(rig.Runtime.State.BlockEntities.TryGet(new BlockRef(construct, _furnaceCell), out var state));
        var station = Assert.IsType<StationBlockState>(state);
        return new FurnaceOutcome(
            CountIn(rig, input, _ore), CountIn(rig, input, _peat), CountIn(rig, output, _ingot),
            (long)station.Started!.Value.Value - (long)started.Value);
    }

    [Fact]
    public async Task Furnace_SettleFrequency_SameOutput()
    {
        // The same furnace with the same input, opened and closed every 7 ticks in one world and not at all in the other: the same output at the end.
        var often = await RunFurnaceAsync(openEvery: 7);
        var never = await RunFurnaceAsync(openEvery: 0);

        Assert.Equal(5, never.Ingots);
        Assert.Equal(300, never.StartedAfterSetUp);
        Assert.Equal(never, often);
    }

    [Fact]
    public async Task Furnace_RestartMidRun_SameResult()
    {
        using var dir = new TempDir();
        ulong hash;
        Tick startedAt;
        Tick tickAtStop;
        ContainerId input;
        ContainerId output;
        EntityId construct;
        await using (var first = new FunctionalBlocksRig(dir.Path, crafting: true))
        {
            await first.StartAsync();
            construct = first.BuildConstruct();
            var alice = await first.JoinAsync(1);
            (input, output, startedAt) = SetUpFurnace(first, construct, ore: 12, peat: 6);

            // Nobody opens the furnace: it just waits in the state while the server runs 100 frames and stops.
            first.StepMany(100);
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
            tickAtStop = first.Runtime.State.CurrentTick;
            Assert.Equal(0, CountIn(first, output, _ingot));
            Assert.NotNull(alice);
        }

        await using var second = new FunctionalBlocksRig(dir.Path, crafting: true);
        await second.StartAsync();
        Assert.Equal(hash, second.Hash);
        Assert.Equal(tickAtStop, second.Runtime.State.CurrentTick);
        (Tick Tick, int Ingots)? firstSettlement = null;
        second.Runtime.Simulation.Applied += applied =>
        {
            if (firstSettlement is null && applied.Operation is CompoundOp compound && compound.Components.Count > 0
                && compound.Components[^1] is BlockEntitySetOp { State: StationBlockState })
            {
                firstSettlement = (applied.Tick, CountIn(second, output, _ingot));
            }
        };
        var alice2 = await second.JoinAsync(1);
        second.StepMany(80);

        await OpenAsync(second, alice2, input, output);

        // The pieces of the first settlement are those of the ticks of the world, across the restart: (tick of the settlement - start) / 60, at most six.
        var settlement = Assert.NotNull(firstSettlement);
        var expected = (int)Math.Min(6UL, (settlement.Tick.Value - startedAt.Value) / 60);
        Assert.True(expected >= 2, "the test must run long enough for two pieces");
        Assert.Equal(expected, settlement.Ingots);
        await second.StopAsync();
    }

    [Fact]
    public async Task Furnace_ServerStopped_TicksDoNotFlow()
    {
        using var dir = new TempDir();
        Tick tickAtStop;
        Tick startedAt;
        EntityId construct;
        await using (var first = new FunctionalBlocksRig(dir.Path, crafting: true))
        {
            await first.StartAsync();
            construct = first.BuildConstruct();
            var alice = await first.JoinAsync(1);
            (_, _, startedAt) = SetUpFurnace(first, construct, ore: 2, peat: 1);
            first.StepMany(20);
            await DisconnectAllAsync(first);
            await first.StopAsync();
            tickAtStop = first.Runtime.State.CurrentTick;
            Assert.True(tickAtStop.Value - startedAt.Value < 60, "less than one piece before the stop");
            Assert.NotNull(alice);
        }

        // Five hours of the clock pass while the server is down: the world does not notice.
        await using var second = new FunctionalBlocksRig(dir.Path, crafting: true);
        second.Time.Advance(TimeSpan.FromHours(5));
        await second.StartAsync();

        Assert.Equal(tickAtStop, second.Runtime.State.CurrentTick);
        Assert.Null(second.Runtime.Simulation.TrySettleStation(new BlockRef(construct, _furnaceCell)));
        Assert.True(second.Runtime.State.BlockEntities.TryGet(new BlockRef(construct, _furnaceCell), out var state));
        Assert.Equal(startedAt, Assert.IsType<StationBlockState>(state).Started);
        await second.StopAsync();
    }

    [Fact]
    public async Task TwoClients_SameFurnace_NoDuplication()
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path, crafting: true);
        await rig.StartAsync();
        var construct = rig.BuildConstruct();
        var alice = await rig.JoinAsync(1);
        var bob = await rig.JoinAsync(2);
        var (input, output, _) = SetUpFurnace(rig, construct, ore: 4, peat: 2);
        foreach (var player in new[] { alice, bob })
        {
            var bag = player.Mirror.IdOf(ContainerKind.PlayerBag);
            rig.Runtime.Simulation.ApplyServerOperation(new ItemCreateOp(new SlotRef(bag, 0), _ore, 40, 500, ItemSource.Admin));
            rig.Runtime.Simulation.ApplyServerOperation(new ItemCreateOp(new SlotRef(bag, 1), _peat, 20, 500, ItemSource.Admin));
            await OpenAsync(rig, player, input, output);
        }

        // Ore plus two ingots, and peat plus one ingot, are constant (two ore and one peat make one ingot).
        var oreInvariant = Total(rig, _ore) + (2 * Total(rig, _ingot));
        var peatInvariant = Total(rig, _peat) + Total(rig, _ingot);
        var random = new Random(32);
        for (var round = 0; round < 300; round++)
        {
            foreach (var player in new[] { alice, bob })
            {
                var intent = RandomIntent(random, player, input, output, construct);
                if (intent is not null)
                {
                    player.Client.SubmitIntent(intent);
                }
            }

            rig.StepMany(3);
        }

        rig.StepMany(30);
        var replies = rig.Players.SelectMany(p => p.Hooks.Replies).GroupBy(r => r.Reason).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(replies.GetValueOrDefault(RejectReason.None) > 100, "too few accepted intents: " + string.Join(", ", replies));
        Assert.Equal(oreInvariant, Total(rig, _ore) + (2 * Total(rig, _ingot)));
        Assert.Equal(peatInvariant, Total(rig, _peat) + Total(rig, _ingot));
        Assert.True(Total(rig, _ingot) > 0, "the furnace made nothing");
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
    }

    [Fact]
    public async Task Discovery_ClientReceivesProgress()
    {
        using var dir = new TempDir();
        await using var rig = new FunctionalBlocksRig(dir.Path, crafting: true);
        await rig.StartAsync();
        var alice = await rig.JoinAsync(1, expectConstruct: false);
        var bob = await rig.JoinAsync(2, expectConstruct: false);
        Assert.True(await rig.RunUntilAsync(() => alice.Hooks.Messages.OfType<PlayerProgressState>().Any() && bob.Hooks.Messages.OfType<PlayerProgressState>().Any()));
        Assert.False(alice.Hooks.Messages.OfType<PlayerProgressState>().Last().Discovered.Contains(_ore));
        var bobStates = bob.Hooks.Messages.OfType<PlayerProgressState>().Count();

        // Ore into the bag of Alice: she hears that she discovered it (the client builds the recipe list from this in stage D); Bob hears nothing.
        rig.Runtime.Simulation.ApplyServerOperation(new ItemCreateOp(new SlotRef(alice.Mirror.IdOf(ContainerKind.PlayerBag), 3), _ore, 1, 500, ItemSource.Admin));
        Assert.True(await rig.RunUntilAsync(() => alice.Hooks.Messages.OfType<PlayerProgressState>().Last().Discovered.Contains(_ore)));

        Assert.Equal(bobStates, bob.Hooks.Messages.OfType<PlayerProgressState>().Count());
    }

    [Fact]
    public async Task GrantKey_Restart_KeyKept()
    {
        using var dir = new TempDir();
        ulong hash;
        await using (var first = new FunctionalBlocksRig(dir.Path, crafting: true))
        {
            await first.StartAsync();
            var alice = await first.JoinAsync(1, expectConstruct: false);
            first.Runtime.Simulation.ApplyServerOperation(new ProgressKeyGrantOp("test:boss_2"));
            Assert.True(await first.RunUntilAsync(() => alice.Hooks.Messages.OfType<PlayerProgressState>().Last().WorldProgressKeys.Contains("test:boss_2")));
            await DisconnectAllAsync(first);
            await first.StopAsync();
            hash = first.Hash;
        }

        await using var second = new FunctionalBlocksRig(dir.Path, crafting: true);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.Equal(["test:boss_2"], second.Runtime.State.ProgressKeys.Sorted);
        var alice2 = await second.JoinAsync(1, expectConstruct: false);
        Assert.True(await second.RunUntilAsync(() => alice2.Hooks.Messages.OfType<PlayerProgressState>().Any()));
        Assert.Equal(["test:boss_2"], alice2.Hooks.Messages.OfType<PlayerProgressState>().Last().WorldProgressKeys);
        await second.StopAsync();
    }

    /// <summary>One random intent of the client around the furnace, judged on what its mirror knows.</summary>
    private static Intent? RandomIntent(Random random, RigPlayer player, ContainerId input, ContainerId output, EntityId construct)
    {
        var bag = player.Mirror.IdOf(ContainerKind.PlayerBag);
        var pick = random.Next(10);
        if (pick == 0)
        {
            return new CraftIntent("t:smelt", 1, new BlockRef(construct, _furnaceCell));
        }

        if (pick <= 2)
        {
            return new TakeAllIntent(output);
        }

        var from = pick <= 4 ? input : bag;
        var slots = player.Mirror.SlotsOf(from);
        var filled = Enumerable.Range(0, slots.Length).Where(i => !slots[i].IsEmpty).ToArray();
        if (filled.Length == 0)
        {
            return null;
        }

        var slot = filled[random.Next(filled.Length)];
        var stack = slots[slot];
        var target = from == input ? bag : input;
        return new ItemMoveIntent(
            new SlotRef(from, (ushort)slot), ItemMoveTarget.AnyIn(target), (ushort)(1 + random.Next(stack.Count)), stack.Type, stack.Count);
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

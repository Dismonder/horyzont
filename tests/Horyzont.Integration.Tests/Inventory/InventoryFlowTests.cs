using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Integration.Tests.Support;
using Horyzont.Persistence.Faults;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Inventory;

/// <summary>Client, in-memory transport, session, simulation and writer: the inventory from the first join to a restart (K6-29W stage C).</summary>
[Collection(SqlitePools.Name)]
public sealed class InventoryFlowTests
{
    private const byte StoneMaterial = 1;

    private static readonly ItemTypeId _gem = WorldFactory.Item(SurvivalContentFactory.GemKey);
    private static readonly ItemTypeId _stone = WorldFactory.Item(SurvivalContentFactory.StoneKey);

    private static ItemMoveIntent Move(ContainerId from, ushort slot, ItemMoveTarget to, ushort count, ItemTypeId type, ushort have) =>
        new(new SlotRef(from, slot), to, count, type, have);

    [Fact]
    public async Task Dig_ClientSeesStoneInDelta()
    {
        using var dir = new TempDir();
        await using var rig = new InventoryRig(dir.Path);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1);
        rig.Terrain.Last.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
        Assert.Equal(new ItemStack(WorldFactory.Item(SurvivalContentFactory.GemKey), 3), rig.Mirror.Slot(ContainerKind.Hotbar, 1));
        Assert.True(rig.Mirror.Slot(ContainerKind.PlayerBag, 0).IsEmpty);

        var reply = await rig.SubmitAsync(rig.DigIntent(player, 1f, 0f));

        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.Equal(1, rig.Mirror.DeltasApplied);
        Assert.Equal(new ItemStack(_stone, 6), rig.Mirror.Slot(ContainerKind.PlayerBag, 0));
        Assert.Contains(rig.Hooks!.Messages, m => m is ContainerDelta { Version: 2 });
        await rig.StopAsync();
    }

    [Fact]
    public async Task MoveSplitMerge_EndToEnd()
    {
        using var dir = new TempDir();
        await using var rig = new InventoryRig(dir.Path);
        await rig.StartAsync();
        await rig.JoinAsync(1);
        var bag = rig.Mirror.IdOf(ContainerKind.PlayerBag);
        var hotbar = rig.Mirror.IdOf(ContainerKind.Hotbar);

        var split = await rig.SubmitAsync(Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(bag, 5)), 1, _gem, 3));
        var merge = await rig.SubmitAsync(Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(bag, 5)), 1, _gem, 2));
        var mismatch = await rig.SubmitAsync(Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(bag, 6)), 1, _gem, 3));
        var rest = await rig.SubmitAsync(Move(hotbar, 1, ItemMoveTarget.AnyIn(bag), 1, _gem, 1));

        Assert.Equal([RejectReason.None, RejectReason.None, RejectReason.ContainerChanged, RejectReason.None], [split.Reason, merge.Reason, mismatch.Reason, rest.Reason]);
        Assert.Equal(new ItemStack(_gem, 3), rig.Mirror.Slot(ContainerKind.PlayerBag, 5));
        Assert.True(rig.Mirror.Slot(ContainerKind.Hotbar, 1).IsEmpty);
        Assert.Equal(WorldFactory.Item(SurvivalContentFactory.ToolKey), rig.Mirror.Slot(ContainerKind.Hotbar, 0).Type);
        foreach (var container in rig.Runtime.State.Containers.All)
        {
            Assert.Equal(container.Slots.ToArray(), rig.Mirror.SlotsOf(container.Id));
            Assert.Equal(container.Version, rig.Mirror.VersionOf(container.Id));
        }

        await rig.StopAsync();
    }

    [Fact]
    public async Task Restart_ItemsIdenticalAndHashEqual()
    {
        using var dir = new TempDir();
        ulong hash;
        ItemStack[] bagSlots;
        ItemStack[] hotbarSlots;
        await using (var first = new InventoryRig(dir.Path))
        {
            await first.StartAsync();
            var player = await first.JoinAsync(1);
            first.Terrain.Last.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
            Assert.Equal(RejectReason.None, (await first.SubmitAsync(first.DigIntent(player, 1f, 0f))).Reason);
            var bag = first.Mirror.IdOf(ContainerKind.PlayerBag);
            var hotbar = first.Mirror.IdOf(ContainerKind.Hotbar);
            Assert.Equal(RejectReason.None, (await first.SubmitAsync(Move(hotbar, 1, ItemMoveTarget.ToSlot(new SlotRef(bag, 9)), 2, _gem, 3))).Reason);
            first.Client!.Disconnect();
            Assert.True(await first.RunUntilAsync(() => !first.Runtime.State.Players.Online.Any()));
            await first.StopAsync();
            hash = first.Hash;
            bagSlots = first.Runtime.State.Containers.TryGetOfPlayer(player, ContainerKind.PlayerBag, out var b) ? b.Slots.ToArray() : [];
            hotbarSlots = first.Runtime.State.Containers.TryGetOfPlayer(player, ContainerKind.Hotbar, out var h) ? h.Slots.ToArray() : [];
        }

        Assert.Equal(new ItemStack(_stone, 6), bagSlots[0]);
        Assert.Equal(new ItemStack(_gem, 2), bagSlots[9]);

        await using var second = new InventoryRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        var again = await second.JoinAsync(1);
        Assert.Equal(3, second.Runtime.State.Containers.Count);
        Assert.Equal(bagSlots, second.Runtime.State.Containers.TryGetOfPlayer(again, ContainerKind.PlayerBag, out var bag2) ? bag2.Slots.ToArray() : []);
        Assert.Equal(hotbarSlots, second.Runtime.State.Containers.TryGetOfPlayer(again, ContainerKind.Hotbar, out var hotbar2) ? hotbar2.Slots.ToArray() : []);
        Assert.Equal(bagSlots, second.Mirror.SlotsOf(second.Mirror.IdOf(ContainerKind.PlayerBag)));
        await second.StopAsync();
    }

    [Theory]
    [InlineData(FaultPoint.BeforeJournalCommit, 6, 1)]
    [InlineData(FaultPoint.AfterJournalCommit, 12, 2)]
    public async Task CrashMidCompound_WholeOrNothing(FaultPoint point, int stoneAfterRestart, int terrainEditsReplayed)
    {
        using var dir = new TempDir();
        var faults = new ArmedFaults();
        await using (var crashed = new InventoryRig(dir.Path, faults))
        {
            await crashed.StartAsync();
            var player = await crashed.JoinAsync(1);
            crashed.Terrain.Last.Terrain.MeasureResult = MeasuredSphere.SolidOf(StoneMaterial, 3);
            Assert.Equal(RejectReason.None, (await crashed.SubmitAsync(crashed.DigIntent(player, 1f, 0f))).Reason);

            // The first dig is durable (its rows are in the file), then the process dies while the second compound is written.
            Assert.True(await crashed.RunUntilAsync(() => RawJournalRows(dir.Path) >= (long)crashed.Runtime.State.LastOpId.Value));
            faults.Arm(point);
            Assert.Equal(RejectReason.None, (await crashed.SubmitAsync(crashed.DigIntent(player, -3f, 0f))).Reason);
            Assert.True(await crashed.RunUntilAsync(() => faults.Crashed));
            Assert.Equal(new ItemStack(_stone, 12), crashed.Mirror.Slot(ContainerKind.PlayerBag, 0));
            crashed.Runtime.RequestShutdown();
            for (var i = 0; i < 8000 && !crashed.Runtime.IsStopped; i++)
            {
                crashed.Step();
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        await using var restarted = new InventoryRig(dir.Path);
        await restarted.StartAsync();

        var player2 = await restarted.JoinAsync(1);
        Assert.True(restarted.Runtime.State.Containers.TryGetOfPlayer(player2, ContainerKind.PlayerBag, out var bag));
        Assert.Equal(new ItemStack(_stone, (ushort)stoneAfterRestart), bag.GetSlot(0));
        Assert.Equal(terrainEditsReplayed, restarted.Terrain.Last.Terrain.AppliedEdits.Count);
        Assert.Equal(3, restarted.Runtime.State.Containers.Count);
        await restarted.StopAsync();
    }

    private static long RawJournalRows(string directory)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = InventoryRig.WorldPath(directory),
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

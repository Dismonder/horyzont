using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Motion;
using Horyzont.Core.Progress;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World;
using Horyzont.Integration.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Integration.Tests.Survival;

/// <summary>
/// A client, the in-memory network, the session, the simulation, the inventory, the progress replication and the vitals service: eating berries moves the item,
/// sets the food slot and raises the maxima, a restart brings the food and the vitals back, the local client of a host gets message 44 and a fall costs health
/// (K6-33 stage C). The start kit has one berry in slot 30 of the bag.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class FoodFlowTests
{
    private const int BerrySlot = 30;
    private static readonly ItemTypeId _berries = WorldFactory.Item(SurvivalContentFactory.BerriesKey);

    [Fact]
    public async Task EatBerries_RemoteClient_DeltaProgressAndVitals()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.HealthMax == 100f), "no vitals of a fresh player arrived");
        var bag = rig.Mirror.IdOf(ContainerKind.PlayerBag);
        Assert.Equal(new ItemStack(_berries, 1), rig.Mirror.Slot(ContainerKind.PlayerBag, BerrySlot));

        var reply = await rig.SubmitAsync(new EatIntent(bag, BerrySlot));

        Assert.Equal(RejectReason.None, reply.Reason);
        Assert.True(await rig.RunUntilAsync(() => rig.Mirror.Slot(ContainerKind.PlayerBag, BerrySlot).IsEmpty), "the berry did not leave the bag");
        Assert.True(await rig.WaitForAsync<PlayerProgressState>(m => m.Food[0].Type == _berries), "the food did not arrive");
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.HealthMax > 105f && m.StaminaMax > 105f), "the maxima did not rise");
        Assert.True(rig.Runtime.State.Progress.TryGet(player, out var progress));
        Assert.Equal(_berries, progress.Food[0].Type);
        Assert.Equal(progress.Food, rig.Latest<PlayerProgressState>()!.Food);

        // The slot is empty now: eating again is refused and nothing changes.
        var again = await rig.SubmitAsync(new EatIntent(bag, BerrySlot));
        Assert.Equal(RejectReason.NotEnoughItems, again.Reason);
        await rig.DisconnectAndStopAsync();
    }

    [Fact]
    public async Task HostLocalClient_ReceivesVitals()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1, "local");
        Assert.True(rig.Runtime.IsLocalPlayer(player));

        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Health == 100f && m.HealthMax == 100f), "the host client got no vitals");
        await rig.DisconnectAndStopAsync();
    }

    [Fact]
    public async Task Restart_VitalsAndFoodRestored()
    {
        using var dir = new TempDir();
        ulong hash;
        PlayerVitals? saved;
        FoodSlots food;
        PlayerId player;
        await using (var first = new FoodRig(dir.Path))
        {
            await first.StartAsync();
            player = await first.JoinAsync(1);
            var bag = first.Mirror.IdOf(ContainerKind.PlayerBag);
            Assert.Equal(RejectReason.None, (await first.SubmitAsync(new EatIntent(bag, BerrySlot))).Reason);
            first.Motion.Set(player, MovementMode.Sprinting);
            first.StepMany(120);
            first.Motion.Set(player, MovementMode.Walking);
            first.StepMany(10);
            await first.DisconnectAndStopAsync();
            Assert.True(first.Runtime.State.Players.TryGet(player, out var state));
            saved = state.Vitals;
            Assert.True(first.Runtime.State.Progress.TryGet(player, out var progress));
            food = progress.Food;
            hash = first.Hash;
        }

        Assert.NotNull(saved);
        // The berries raise the maximum of stamina to about 120; the sprint left it well below, and the health regenerated above the base.
        Assert.True(saved.Value.Stamina < 115f, "sprinting should have spent stamina: " + saved);
        Assert.True(saved.Value.Health > 100f, "the food should have raised the health: " + saved);
        Assert.Equal(_berries, food[0].Type);

        await using var second = new FoodRig(dir.Path);
        await second.StartAsync();

        Assert.Equal(hash, second.Hash);
        Assert.True(second.Runtime.State.Players.TryGet(player, out var restored));
        Assert.Equal(saved, restored.Vitals);
        Assert.True(second.Runtime.State.Progress.TryGet(player, out var restoredProgress));
        Assert.Equal(food, restoredProgress.Food);
        await second.StopAsync();
    }

    [Fact]
    public async Task FallFromTower_RemoteClient_HealthDrops()
    {
        using var dir = new TempDir();
        await using var rig = new FoodRig(dir.Path);
        await rig.StartAsync();
        var player = await rig.JoinAsync(1);
        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => m.Health == 100f), "no vitals arrived");

        rig.Motion.Set(player, MovementMode.Falling, new Vector3(0f, -15f, 0f));
        rig.StepMany(2);
        rig.Motion.Set(player, MovementMode.Walking);
        rig.StepMany(2);

        Assert.True(await rig.WaitForAsync<PlayerVitalsMessage>(m => MathF.Abs(m.Health - 68f) < 0.5f), "the fall did not cost 32 health");
        await rig.DisconnectAndStopAsync();
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

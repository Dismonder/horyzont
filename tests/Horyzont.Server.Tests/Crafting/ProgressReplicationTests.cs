using Horyzont.Core.Ids;
using Horyzont.Core.Items;
using Horyzont.Core.Operations;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Server.Tests.Crafting;

/// <summary>
/// <see cref="Horyzont.Server.Crafting.ProgressReplication"/> (K6-32 D11): the discoveries of a player reach that player only, a progress key reaches
/// every player online, and the first state comes after the container states.
/// </summary>
public sealed class ProgressReplicationTests
{
    private readonly CraftingServerRig _rig = new();

    private static CraftingServerRig WithOreInBag() => new(sim =>
    {
        sim.ApplyServerOperation(WorldFactory.PlayerContainers(new PlayerId(1), 1));
        sim.ApplyServerOperation(new ItemCreateOp(new SlotRef(WorldFactory.BagOf(1), 0), CraftingServerRig.Ore, 1, 500, ItemSource.Admin));
    });

    private static ItemTypeId Gem => WorldFactory.Item(SurvivalContentFactory.GemKey);

    private static ItemTypeId Tool => WorldFactory.Item(SurvivalContentFactory.ToolKey);

    private static PlayerProgressState LastProgress(CraftingServerRig rig, PlayerId player) => rig.SentTo(player).OfType<PlayerProgressState>().Last();

    [Fact]
    public void PlayerReady_SendsProgressAfterContainers()
    {
        _rig.Inventory.OnPlayerReady(_rig.Alice);
        _rig.Progress.OnPlayerReady(_rig.Alice);

        var messages = _rig.SentTo(_rig.Alice);
        var progress = Assert.Single(messages.OfType<PlayerProgressState>());
        Assert.IsType<PlayerProgressState>(messages[^1]);
        Assert.Equal(3, messages.OfType<ContainerState>().Count());

        // The start kit (the tool and three gems) was discovered when it was put into the hotbar; nothing else, and the world has no keys.
        Assert.True(progress.Discovered.Contains(Tool) && progress.Discovered.Contains(Gem));
        Assert.Equal(2, progress.Discovered.Count);
        Assert.Empty(progress.WorldProgressKeys);
        Assert.Equal(1, _rig.Progress.StatesSent);
    }

    [Fact]
    public void PlayerReady_HeldItemsFromBeforeDiscoveries_AreDiscoveredFirst()
    {
        // The bag holds ore that was put there before the discoveries existed (a world saved by K6-31): the player has discovered nothing yet.
        var rig = WithOreInBag();
        Assert.False(rig.Sim.State.Progress.TryGet(rig.Alice, out _));

        rig.Inventory.OnPlayerReady(rig.Alice);
        rig.Progress.OnPlayerReady(rig.Alice);

        var state = LastProgress(rig, rig.Alice);
        Assert.True(state.Discovered.Contains(CraftingServerRig.Ore));
        Assert.IsType<PlayerProgressState>(rig.SentTo(rig.Alice)[^1]);
    }

    [Fact]
    public void Discovery_SendsToOwnerOnly()
    {
        _rig.ReadyBoth();
        _rig.Sent.Clear();

        // Ore into the bag of Alice: the simulation discovers it for her in the operation after, and only she hears of it.
        _rig.Stock(WorldFactory.BagOf(1), 5, CraftingServerRig.Ore, 2);

        var state = Assert.Single(_rig.SentTo(_rig.Alice).OfType<PlayerProgressState>());
        Assert.True(state.Discovered.Contains(CraftingServerRig.Ore));
        Assert.Empty(_rig.SentTo(_rig.Bob).OfType<PlayerProgressState>());
    }

    [Fact]
    public void DiscoveryInCompound_StillSent()
    {
        _rig.ReadyBoth();
        _rig.Sent.Clear();

        // A discovery that is a component of a compound (the result of a craft) is found by OperationProgress, not by the type of the operation.
        _rig.Sim.ApplyServerOperation(new CompoundOp(
        [
            new ItemDiscoverOp(_rig.Bob, CraftingServerRig.Ingot),
            new ItemDiscoverOp(_rig.Alice, CraftingServerRig.Peat),
        ]));

        Assert.True(Assert.Single(_rig.SentTo(_rig.Alice).OfType<PlayerProgressState>()).Discovered.Contains(CraftingServerRig.Peat));
        Assert.True(Assert.Single(_rig.SentTo(_rig.Bob).OfType<PlayerProgressState>()).Discovered.Contains(CraftingServerRig.Ingot));
    }

    [Fact]
    public void GrantKey_SendsToAllOnline()
    {
        _rig.ReadyBoth();
        _rig.Sent.Clear();

        _rig.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:boss_2"));

        foreach (var player in new[] { _rig.Alice, _rig.Bob })
        {
            var state = Assert.Single(_rig.SentTo(player).OfType<PlayerProgressState>());
            Assert.Equal(["test:boss_2"], state.WorldProgressKeys);
        }
    }

    [Fact]
    public void PlayerLeft_NothingSentToThem()
    {
        _rig.ReadyBoth();
        _rig.Progress.OnPlayerLeaving(_rig.Bob, DisconnectCode.Normal);
        _rig.Sent.Clear();

        _rig.Sim.ApplyServerOperation(new ProgressKeyGrantOp("test:boss_2"));

        Assert.Single(_rig.SentTo(_rig.Alice).OfType<PlayerProgressState>());
        Assert.Empty(_rig.SentTo(_rig.Bob).OfType<PlayerProgressState>());
    }

    [Fact]
    public async Task Host_LocalPlayer_GetsProgressState()
    {
        await using var rig = new ServerRig(mode: ProcessMode.Host);
        var progress = Horyzont.Server.Crafting.ProgressReplication.Register(rig.Runtime);
        await rig.StartAsync();

        var local = await rig.JoinAsync("local", 1);
        var remote = await rig.JoinAsync("remote", 2);
        rig.StepMany(5);

        Assert.True(rig.Runtime.IsLocalPlayer(local.Player));
        foreach (var client in new[] { local, remote })
        {
            Assert.Empty(Assert.Single(client.Received.OfType<PlayerProgressState>()).WorldProgressKeys);
        }

        rig.Runtime.Simulation.ApplyServerOperation(new ProgressKeyGrantOp("test:boss"));
        rig.StepMany(5);

        foreach (var client in new[] { local, remote })
        {
            Assert.Equal(["test:boss"], client.Received.OfType<PlayerProgressState>().Last().WorldProgressKeys);
        }

        Assert.Equal(4, progress.StatesSent);
    }
}

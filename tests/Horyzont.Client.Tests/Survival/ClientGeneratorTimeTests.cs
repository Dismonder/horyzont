using Horyzont.Client.Blocks;
using Horyzont.Client.Survival;
using Horyzont.Core.BlockEntities;
using Horyzont.Core.Ids;
using Horyzont.Core.Intents;
using Horyzont.Core.Items;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.World.Constructs;
using Horyzont.Testing;

namespace Horyzont.Client.Tests.Survival;

/// <summary>
/// The time an oxygen generator still runs, as the client works it out for the panel (K6-34 D19): the same pure function as the server (<c>OxygenGeneratorProcess.RemainingTicks</c>) over the state of the block
/// and the fuel in its input, at the tick of the server, with no prediction. The generator of the content burns a cell for 10 seconds (600 ticks).
/// </summary>
public sealed class ClientGeneratorTimeTests
{
    private const ulong CellTicks = 600;
    private static readonly EntityId _construct = new(100);
    private static readonly CellPos _cell = new(5, 1, 0);
    private static readonly ContainerId _input = new(20);

    private readonly HazardClientFixture _fx = new();
    private readonly ClientBlockEntities _entities = new();
    private readonly ClientGeneratorTime _time;

    public ClientGeneratorTimeTests() => _time = new ClientGeneratorTime(_fx.Inventory, _entities, _fx.Content.Hazards, 60);

    private void Generator(Tick? started, ushort queued)
    {
        _entities.Apply(new BlockEntityStates(_construct, [new BlockEntityEntry(_cell, new OxygenGeneratorBlockState(_input, started))]));
        _fx.Inventory.OnMessage(
            new ContainerState(
                _input, RejectReason.None, ContainerKind.StationInput, ContainerAccess.Shared, 2, 1,
                queued == 0 ? [] : [new SlotContent(0, new ItemStack(WorldFactory.Item(SurvivalContentFactory.FuelPackKey), queued))]));
    }

    [Fact]
    public void Running_CurrentCellOnly()
    {
        Generator(new Tick(1_000), queued: 0);

        // 100 ticks into the burning cell: 500 are left.
        Assert.True(_time.TryGetRemaining(_construct, _cell, 1_100, out var ticks));
        Assert.Equal(CellTicks - 100, ticks);
    }

    [Fact]
    public void Running_QueuedCellsAdd()
    {
        Generator(new Tick(1_000), queued: 2);

        Assert.True(_time.TryGetRemaining(_construct, _cell, 1_100, out var ticks));
        Assert.Equal(CellTicks - 100 + (2 * CellTicks), ticks);
    }

    [Fact]
    public void Idle_WithoutFuel_Zero()
    {
        Generator(null, queued: 0);

        Assert.True(_time.TryGetRemaining(_construct, _cell, 1_100, out var ticks));
        Assert.Equal(0UL, ticks);
    }

    [Fact]
    public void Idle_WithFuel_ZeroUntilStarted()
    {
        Generator(null, queued: 1);

        // An idle generator does not run: the next settlement starts it, and until then the panel shows nothing left.
        Assert.True(_time.TryGetRemaining(_construct, _cell, 1_100, out var ticks));
        Assert.Equal(0UL, ticks);
    }

    [Fact]
    public void UnknownState_NoAnswer()
    {
        Assert.False(_time.TryGetRemaining(_construct, _cell, 1_100, out var ticks));
        Assert.Equal(0UL, ticks);
    }

    [Fact]
    public void AfterTheLastCell_Zero()
    {
        Generator(new Tick(1_000), queued: 0);

        Assert.True(_time.TryGetRemaining(_construct, _cell, 1_000 + CellTicks + 50, out var ticks));
        Assert.Equal(0UL, ticks);
    }
}

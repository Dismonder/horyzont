using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Ships.Flight;
using Horyzont.Net.Session;

namespace Horyzont.Net.Tests.Session;

/// <summary>K5-25 decision 12 and R1: the generic buffer with ship inputs, slots and the session API.</summary>
public sealed class SequencedInputBufferTests
{
    private static ShipInput Input(uint tick) => new(tick, new ShipControl((sbyte)(tick % 100), 0, 0, 0, 0, 0, ShipButtons.Assist));

    [Fact]
    public void SequencedInputBuffer_ReorderedArrival_SortedAndNewestDroppedWhenFull()
    {
        var buffer = new SequencedInputBuffer<ShipInput>(4);

        buffer.Add(5, [Input(5), Input(6), Input(7)]);
        buffer.Add(1, [Input(1), Input(2)]);

        var taken = new List<uint>();
        while (buffer.TryTake(out var input, out var seq))
        {
            Assert.Equal(seq, input.ClientTick);
            taken.Add(seq);
        }

        Assert.Equal([1u, 2, 5, 6], taken);
    }

    [Fact]
    public void SequencedInputBuffer_TakeSlot_DropsOlderRefusesLate()
    {
        var buffer = new SequencedInputBuffer<ShipInput>(8);
        buffer.Add(3, [Input(3), Input(4)]);
        buffer.Add(6, [Input(6)]);

        Assert.True(buffer.TryTakeSlot(4, out var four));
        Assert.Equal(4u, four.ClientTick);
        Assert.False(buffer.TryTakeSlot(5, out _));
        Assert.True(buffer.TryPeekNewest(out var newest));
        Assert.Equal(6u, newest);

        // Slot 5 is consumed: its late arrival is refused and counted.
        buffer.Add(5, [Input(5)]);
        Assert.Equal(1, buffer.LateCount);
        Assert.Equal(1, buffer.Count);

        buffer.Clear();
        buffer.Add(1, [Input(1)]);
        Assert.True(buffer.TryTakeSlot(1, out _));
        Assert.Equal(0, buffer.LateCount);
    }

    [Fact]
    public void ServerSession_ShipInputs_OnePerSlotAndClear()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);

        for (uint first = 1; first <= 6; first++)
        {
            client.Session.Send(new ShipInputBatch(first, [Input(first), Input(first + 1), Input(first + 2)]));
        }

        h.Step();

        Assert.True(h.Server.TryPeekNewestShipSeq(player, out var newest));
        Assert.Equal(8u, newest);
        Assert.True(h.Server.TryTakeShipInput(player, 6, out var six));
        Assert.Equal(6u, six.ClientTick);
        Assert.False(h.Server.TryTakeShipInput(player, 6, out _));

        h.Server.ClearShipInputs(player);
        Assert.False(h.Server.TryPeekNewestShipSeq(player, out _));
        Assert.Equal(0, h.Server.ShipInputLateCount(player));
    }
}

using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport.InMemory;

namespace Horyzont.Net.Tests.Session;

public sealed class InputAndLinkTests
{
    [Fact]
    public void TryTakeInput_DuplicatesAndBurst_OnePerCallUpToCapacity()
    {
        using var h = new SessionHarness();
        var client = h.JoinInGame();
        var player = client.Player(h);

        // The client repeats its last three inputs in every batch: seqs 1..3, 2..4, ..., 10..12.
        for (uint first = 1; first <= 10; first++)
        {
            client.Session.SendInputs(first, [Input(first), Input(first + 1), Input(first + 2)]);
        }

        h.Step();

        var taken = new List<uint>();
        while (h.Server.TryTakeInput(player, out var input, out var seq))
        {
            Assert.Equal(seq, input.ClientTick);
            taken.Add(seq);
        }

        Assert.Equal([1u, 2, 3, 4, 5, 6, 7, 8], taken);

        // Older and already processed inputs are ignored; newer ones are accepted again.
        client.Session.SendInputs(7, [Input(7), Input(8), Input(9)]);
        h.Step();
        Assert.True(h.Server.TryTakeInput(player, out _, out var next));
        Assert.Equal(9u, next);
        Assert.False(h.Server.TryTakeInput(player, out _, out _));
    }

    [Fact]
    public void InputBuffer_ReorderedArrival_SortedAndNewestDroppedWhenFull()
    {
        var buffer = new InputBuffer(4);

        buffer.Add(5, [Input(5), Input(6), Input(7)]);
        buffer.Add(1, [Input(1), Input(2)]);

        var taken = new List<uint>();
        while (buffer.TryTake(out _, out var seq))
        {
            taken.Add(seq);
        }

        Assert.Equal([1u, 2, 5, 6], taken);
    }

    [Fact]
    public void InputBuffer_SeqOverflow_IsIgnored()
    {
        var buffer = new InputBuffer(8);

        buffer.Add(uint.MaxValue, [Input(1), Input(2)]);

        Assert.True(buffer.TryTake(out _, out var seq));
        Assert.Equal(uint.MaxValue, seq);
        Assert.False(buffer.TryTake(out _, out _));
    }

    [Fact]
    public void LossyLink_500Intents_RepliesInOrder()
    {
        var lossy = new LinkConditions(TimeSpan.FromMilliseconds(40), TimeSpan.FromMilliseconds(20), 0.05, 0, 99);
        using var h = new SessionHarness(conditions: lossy);
        var client = h.JoinInGame();

        var sent = new List<uint>();
        while (sent.Count < 500)
        {
            for (var i = 0; i < 10; i++)
            {
                sent.Add(client.Session.SubmitIntent(SessionHarness.SampleIntent()));
            }

            client.Session.SendInputs((uint)sent.Count, [Input((uint)sent.Count)]);
            h.Step(TimeSpan.FromMilliseconds(33));
        }

        Assert.True(h.RunUntil(() => client.Received<IntentReply>().Count() == 500, TimeSpan.FromSeconds(10)));

        Assert.Equal(sent, client.Received<IntentReply>().Select(r => r.ClientSeq));
        Assert.Equal(ClientState.InGame, client.Session.State);
    }

    private static PlayerInput Input(uint tick) => new(tick, 0.5f, 0, 0, 0, InputButtons.None);
}

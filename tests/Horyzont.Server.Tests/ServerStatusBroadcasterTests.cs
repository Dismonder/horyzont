using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Server.UI;

namespace Horyzont.Server.Tests;

public sealed class ServerStatusBroadcasterTests
{
    [Fact]
    public void Status_WriterFaultThenCommit_BroadcastsTransitionsOnce()
    {
        ServerStatusInfo status = new(false, 0, "Running");
        var sent = new List<(PlayerId Player, ServerStatus Status)>();
        var broadcaster = new ServerStatusBroadcaster(() => status, (player, message) => sent.Add((player, message)));
        broadcaster.OnPlayerReady(new PlayerId(1));
        broadcaster.OnPlayerReady(new PlayerId(2));
        Assert.Equal(2, sent.Count);
        broadcaster.OnTick(null!);
        Assert.Equal(2, sent.Count);
        status = new(true, 2, "Writer unavailable");
        broadcaster.OnTick(null!);
        broadcaster.OnTick(null!);
        Assert.Equal(4, sent.Count);
        broadcaster.OnPlayerReady(new PlayerId(3));
        Assert.True(sent[^1].Status.ReadOnly);
        broadcaster.OnPlayerLeaving(new PlayerId(3), DisconnectCode.Normal);
        status = new(false, 0, "Running");
        broadcaster.OnTick(null!);
        broadcaster.OnTick(null!);
        Assert.Equal(7, sent.Count);
        Assert.All(sent.TakeLast(2), entry => Assert.False(entry.Status.ReadOnly));
    }
}

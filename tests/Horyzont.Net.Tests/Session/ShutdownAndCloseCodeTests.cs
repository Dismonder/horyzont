using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Net.Transport;

namespace Horyzont.Net.Tests.Session;

/// <summary>E4, E8 and E9 of the K1 gate review (K2-10 entry conditions).</summary>
public sealed class ShutdownAndCloseCodeTests
{
    private static readonly TimeSpan _fiveSeconds = TimeSpan.FromSeconds(5);

    [Fact]
    public void HandleHello_HelloForOtherPlayer_ClosesPeerAndCallsOnPlayerLeft()
    {
        using var h = new SessionHarness();
        h.ServerHooks.HelloPlayer = player => new PlayerId(player.Value + 40);
        var client = h.AddClient();

        Assert.True(h.RunUntil(() => client.Session.State == ClientState.Disconnected, _fiveSeconds));

        var joined = Assert.Single(h.ServerHooks.Joined);
        Assert.Equal((joined.Player, DisconnectCode.ProtocolViolation), Assert.Single(h.ServerHooks.Left));
        Assert.Empty(h.Server.PlayersInGame);
        Assert.Null(client.Hooks.Hello);
    }

    [Fact]
    public void Connect_DuringShutdownWithFullServer_GetsShutdownCodeNotServerFull()
    {
        using var h = new SessionHarness(new ServerOptions { BuildHash = SessionHarness.Build, MaxPendingConnections = 1 });
        var first = h.AddRaw();
        Assert.True(h.RunUntil(() => first.IsConnected, _fiveSeconds));

        h.Server.Shutdown(DisconnectCode.HostLeft);
        var late = h.AddRaw();
        Assert.True(h.RunUntil(() => late.IsClosed, _fiveSeconds));

        var frame = Assert.Single(late.Messages());
        Assert.Equal(DisconnectCode.HostLeft, Assert.IsType<Disconnect>(frame).Code);
        Assert.True(h.RunUntil(() => h.Server.IsShutdownComplete, _fiveSeconds));
    }

    [Theory]
    [InlineData(DisconnectCode.Kicked)]
    [InlineData(DisconnectCode.HostLeft)]
    [InlineData(DisconnectCode.ServerShutdown)]
    public void ClientDisconnectWithForeignCode_OnPlayerLeftGetsNormal(DisconnectCode claimed)
    {
        using var h = new SessionHarness();
        var raw = h.AddRaw();
        Assert.True(h.RunUntil(() => raw.IsConnected, _fiveSeconds));
        raw.Send(TransportChannel.ReliableOrdered, SessionHarness.Encode(new ClientHello(
            ProtocolConstants.ProtocolVersion, SessionHarness.Build, SessionHarness.DevIdentity(77), "x", "", [])));
        Assert.True(h.RunUntil(() => h.ServerHooks.Joined.Count == 1, _fiveSeconds));

        raw.Send(TransportChannel.ReliableOrdered, SessionHarness.Encode(new Disconnect(claimed)));

        Assert.True(h.RunUntil(() => h.ServerHooks.Left.Count == 1, _fiveSeconds));
        Assert.Equal(DisconnectCode.Normal, h.ServerHooks.Left[0].Reason);
    }
}

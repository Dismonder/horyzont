using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Space;
using Horyzont.Server.Space;

namespace Horyzont.Server.Tests.Space;

public sealed class ServerSpaceSyncTests
{
    [Fact]
    public void ServerSpaceSync_PlayerReady_SendsStarSystemStateOnce()
    {
        var system = StarSystemGenesis.CreateWorldBodies(4, "core:test/1").System;
        var sent = new List<(PlayerId Player, Message Message)>();
        var sync = new ServerSpaceSync(() => system, (player, message) => sent.Add((player, message)));
        var alice = new PlayerId(1);
        var bob = new PlayerId(2);

        sync.OnPlayerReady(alice);
        sync.OnPlayerReady(alice);
        sync.OnPlayerReady(bob);

        Assert.Equal([alice, bob], sent.Select(s => s.Player));
        Assert.All(sent, s => Assert.Equal(new StarSystemState(system), s.Message));

        sync.OnPlayerLeaving(alice, DisconnectCode.Normal);
        sync.OnPlayerReady(alice);
        Assert.Equal(3, sent.Count);
        Assert.Equal(3, sync.Sent);
    }

    [Fact]
    public void ServerSpaceSync_NoStarSystem_SendsNothing()
    {
        var sent = 0;
        var sync = new ServerSpaceSync(() => null, (_, _) => sent++);

        sync.OnPlayerReady(new PlayerId(1));

        Assert.Equal(0, sent);
    }
}

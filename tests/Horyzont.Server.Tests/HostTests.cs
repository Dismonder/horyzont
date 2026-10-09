using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Persistence;
using Horyzont.Server.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

[Collection(SqlitePools.Name)]
public sealed class HostTests
{
    [Fact]
    public async Task LocalClientAndRemoteClient_BothReachInGame_LocalIsLocalPlayer()
    {
        await using var rig = new ServerRig(mode: ProcessMode.Host);
        await rig.StartAsync();

        var local = await rig.JoinAsync("local", 1);
        var remote = await rig.JoinAsync("remote", 2);

        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.Session.PlayersInGame.Count == 2));
        Assert.True(rig.Runtime.IsLocalPlayer(local.Player));
        Assert.False(rig.Runtime.IsLocalPlayer(remote.Player));
        Assert.False(rig.Runtime.IsLocalPlayer(new PlayerId(99)));
        await rig.StopAsync();
        Assert.False(rig.Runtime.IsLocalPlayer(local.Player));
    }

    [Fact]
    public async Task HostLeaves_RemoteClientGetsHostLeft_WorldSaved()
    {
        using var dir = new TempDirectory();
        await using (var rig = new ServerRig(dir, mode: ProcessMode.Host))
        {
            await rig.StartAsync();
            var local = await rig.JoinAsync("local", 1);
            var remote = await rig.JoinAsync("remote", 2);
            await rig.DigAsync(local, 3f, 1f);

            await rig.StopAsync();

            Assert.Equal([DisconnectCode.HostLeft], remote.Disconnections);
            Assert.Equal([DisconnectCode.HostLeft], local.Disconnections);
            Assert.Equal(0, rig.Runtime.ExitCode);
        }

        var loaded = WorldLoader.Load(
            dir.WorldPath, new WorldLoadOptions { BackupsDirectory = dir.BackupsPath }, new FakeTimeProvider(), NullLogger.Instance);
        Assert.Empty(loaded.JournalTail);
        Assert.Equal(2, loaded.State.Players.Count);
        Assert.Empty(loaded.State.Players.Online);
        Assert.Equal(loaded.State.LastOpId, loaded.LastCheckpointOpId);
    }

    [Fact]
    public async Task Solo_DoesNotNeedAnyListener_ServerRunsWithTheLocalClientOnly()
    {
        await using var rig = new ServerRig(mode: ProcessMode.Solo);
        await rig.StartAsync();

        var local = await rig.JoinAsync("local", 1);

        Assert.True(rig.Runtime.IsLocalPlayer(local.Player));
        await rig.StopAsync();
        Assert.Equal([DisconnectCode.HostLeft], local.Disconnections);
    }
}

using Horyzont.Core.Hashing;
using Horyzont.Core.Protocol;
using Horyzont.Server.Console;
using Horyzont.Server.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Server.Tests;

[Collection(SqlitePools.Name)]
public sealed class ConsoleCommandProcessorTests
{
    [Fact]
    public async Task Save_QueuesACheckpoint()
    {
        await using var rig = await StartedAsync();
        var processor = Processor(rig);
        var terrain = rig.Terrain.Last;

        var reply = processor.Execute("save");

        Assert.Equal("save queued", reply);
        Assert.True(await rig.RunUntilAsync(() => terrain.CommittedCheckpoints == 2));
        Assert.True(rig.Log.Contains("Checkpoint started boundary="));
        Assert.True(rig.Log.Contains("Checkpoint committed boundary="));
        await rig.StopAsync();
    }

    [Fact]
    public async Task Stats_AfterTicks_PrintsTickWindowLastOpAndCheckpointState()
    {
        await using var rig = await StartedAsync();
        var processor = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance, devCommands: true);
        await rig.JoinAsync("a", 1);
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.Session.PlayersInGame.Count == 1));
        processor.Execute("dev-op");
        _ = processor.Execute("stats");
        rig.StepMany(50);

        var reply = processor.Execute("stats");

        var match = System.Text.RegularExpressions.Regex.Match(
            reply, @"^STATS tick_ms mean=(\d+\.\d{3}) p95=(\d+\.\d{3}) p99=(\d+\.\d{3}) max=(\d+\.\d{3}) n=(\d+) players=1 last_op=(\d+) checkpoint=(idle|running)$");
        Assert.True(match.Success, reply);
        Assert.True(int.Parse(match.Groups[5].Value, System.Globalization.CultureInfo.InvariantCulture) >= 50);
        Assert.Equal(rig.Runtime.State.LastOpId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), match.Groups[6].Value);

        // The window restarts with every stats command.
        var second = processor.Execute("stats");
        Assert.Contains(" n=0 ", second, StringComparison.Ordinal);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Stop_BeginsTheShutdown()
    {
        await using var rig = await StartedAsync();
        var processor = Processor(rig);

        var reply = processor.Execute("stop");

        Assert.Equal("stopping", reply);
        Assert.Equal(ServerRuntimeState.ShuttingDown, rig.Runtime.RuntimeState);
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.IsStopped));
        Assert.Equal("stopping", processor.Execute("stop"));
    }

    [Fact]
    public async Task Players_ListsPlayersInGame()
    {
        await using var rig = await StartedAsync();
        var processor = Processor(rig);
        Assert.Equal("0 players", processor.Execute("players"));

        var client = await rig.JoinAsync("local", 1);
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.Session.PlayersInGame.Count == 1));
        var reply = processor.Execute("PLAYERS");

        Assert.StartsWith("1 players", reply, StringComparison.Ordinal);
        Assert.Contains($"{client.Player.Value}\tlocal\tDev:1\t", reply, StringComparison.Ordinal);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Kick_DisconnectsThePlayerWithKicked()
    {
        await using var rig = await StartedAsync();
        var processor = Processor(rig);
        var first = await rig.JoinAsync("a", 1);
        var second = await rig.JoinAsync("b", 2);
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.Session.PlayersInGame.Count == 2));

        Assert.Equal($"kicked {second.Player.Value}", processor.Execute($"kick {second.Player.Value}"));
        Assert.Equal("usage: kick <player id>", processor.Execute("kick x"));
        Assert.Equal("usage: kick <player id>", processor.Execute("kick"));
        Assert.Equal("usage: kick <player id>", processor.Execute("kick -1"));
        Assert.Equal("unknown player 999", processor.Execute("kick 999"));

        Assert.True(await rig.RunUntilAsync(() => second.Disconnections.Count == 1));
        Assert.Equal([DisconnectCode.Kicked], second.Disconnections);
        Assert.Empty(first.Disconnections);
        await rig.StopAsync();
    }

    [Fact]
    public async Task DevOp_WithDevCommands_JournalsAChangeOfTheWorld()
    {
        await using var rig = await StartedAsync();
        var processor = new ConsoleCommandProcessor(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance, devCommands: true);
        var before = StateHasher.Hash(rig.Runtime.State);
        var lastOp = rig.Runtime.State.LastOpId;

        var first = processor.Execute("dev-op");
        var second = processor.Execute("dev-op");

        Assert.StartsWith("dev-op applied", first, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.NotEqual(lastOp, rig.Runtime.State.LastOpId);
        Assert.NotEqual(before, StateHasher.Hash(rig.Runtime.State));
        Assert.Empty(rig.Runtime.State.Players.Online);
        await rig.StopAsync();
    }

    [Fact]
    public async Task DevOp_WithoutDevCommands_IsUnknownAndChangesNothing()
    {
        await using var rig = await StartedAsync();
        var before = StateHasher.Hash(rig.Runtime.State);

        var reply = Processor(rig).Execute("dev-op");

        Assert.Contains("commands:", reply, StringComparison.Ordinal);
        Assert.Equal(before, StateHasher.Hash(rig.Runtime.State));
        await rig.StopAsync();
    }

    [Theory]
    [InlineData("help")]
    [InlineData("frobnicate")]
    [InlineData("  HELP  ")]
    public async Task UnknownCommandAndHelp_ShowTheCommandList(string line)
    {
        await using var rig = await StartedAsync();

        var reply = Processor(rig).Execute(line);

        Assert.Contains("commands:", reply, StringComparison.Ordinal);
        Assert.Contains("kick <id>", reply, StringComparison.Ordinal);
        await rig.StopAsync();
    }

    [Fact]
    public async Task EmptyAndOverlongLines_AreHandledWithoutEffect()
    {
        await using var rig = await StartedAsync();
        var processor = Processor(rig);

        Assert.Equal(string.Empty, processor.Execute(string.Empty));
        Assert.Equal(string.Empty, processor.Execute("   "));
        Assert.Equal(string.Empty, processor.Execute(null!));
        Assert.StartsWith("line too long", processor.Execute("save " + new string('x', 300)), StringComparison.Ordinal);
        Assert.Equal(ServerRuntimeState.Running, rig.Runtime.RuntimeState);
        Assert.Single(rig.Terrain.Last.Requests);
        await rig.StopAsync();
    }

    [Fact]
    public async Task Commands_BeforeStart_AnswerNotRunning()
    {
        await using var rig = new ServerRig();
        var processor = Processor(rig);

        Assert.Equal("the server is not running", processor.Execute("save"));
        Assert.Equal("the server is not running", processor.Execute("players"));
        Assert.Equal("the server is not running", processor.Execute("kick 1"));
        await Task.CompletedTask;
    }

    private static async Task<ServerRig> StartedAsync()
    {
        var rig = new ServerRig(mode: ProcessMode.DedicatedServer);
        await rig.StartAsync();
        return rig;
    }

    private static ConsoleCommandProcessor Processor(ServerRig rig) => new(rig.Runtime, NullLogger<ConsoleCommandProcessor>.Instance);
}

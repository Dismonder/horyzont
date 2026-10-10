using Horyzont.Core.Protocol.Messages;
using Horyzont.Net.Session;
using Horyzont.Persistence.Faults;
using Horyzont.Server.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Server.Tests;

public sealed class ServerStatusIntegrationTests
{
    [Fact]
    public async Task Status_WriterFaultThenCommit_TwoClientsGetEachTransitionOnce()
    {
        await using var rig = new ServerRig();
        await rig.StartAsync();
        var first = await rig.JoinAsync("a", 1);
        var second = await rig.JoinAsync("b", 2);
        rig.StepMany(30);
        Assert.All([first, second], c => Assert.Equal([false], Statuses(c).Select(s => s.ReadOnly)));

        rig.Faults.DiskErrorAt(FaultPoint.BeforeJournalCommit);
        await Scenarios.DigManyAsync(rig, first, 1);
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.RuntimeState == ServerRuntimeState.SafeMode));
        rig.StepMany(30);
        Assert.All([first, second], c => Assert.Equal([false, true], Statuses(c).Select(s => s.ReadOnly)));
        Assert.Equal(2, Statuses(first)[^1].Reason);

        // A join during safe mode is refused before ServerHello with the writer-fault reason.
        var late = rig.AddClient("late", 3);
        Assert.True(await rig.RunUntilAsync(() => late.Session.State == ClientState.Disconnected));
        Assert.Null(late.Hello);
        var refusal = Assert.IsType<JoinUnavailable>(Assert.Single(late.Messages));
        Assert.Equal(2, refusal.Reason);

        // Running is announced only after the writer has really recovered.
        rig.Runtime.RequestCheckpoint();
        Assert.True(await rig.RunUntilAsync(() => rig.Runtime.RuntimeState == ServerRuntimeState.Running));
        rig.StepMany(30);
        Assert.All([first, second], c => Assert.Equal([false, true, false], Statuses(c).Select(s => s.ReadOnly)));
        await rig.StopAsync();
    }

    [Fact]
    public async Task Join_TerrainSemanticsMismatch_ReceivesReasonOne()
    {
        using var dir = new TempDirectory();
        await using (var crashed = await Scenarios.CrashedRunAsync(dir, intents: 6))
        {
            await crashed.FinishCrashedRunAsync();
        }

        var factory = new FakeServerTerrainFactory(new FakeTimeProvider()) { SemanticsVersion = 2 };
        await using var rig = new ServerRig(dir, terrain: factory);
        Assert.IsType<WorldStartResult.StartedReadOnly>(await rig.StartAsync());
        Assert.Equal(1, rig.Runtime.CurrentStatus.Reason);

        var client = rig.AddClient("local", 1);
        Assert.True(await rig.RunUntilAsync(() => client.Session.State == ClientState.Disconnected));
        Assert.Null(client.Hello);
        Assert.Equal(1, Assert.IsType<JoinUnavailable>(Assert.Single(client.Messages)).Reason);
        await rig.StopAsync();
    }

    private static List<ServerStatus> Statuses(RigClient client) => [.. client.Messages.OfType<ServerStatus>()];
}

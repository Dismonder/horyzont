using Horyzont.Core.Intents;
using Horyzont.Persistence.Faults;

namespace Horyzont.Server.Tests.Support;

/// <summary>Reusable runs of a server: edits by clients and a server that dies with players online.</summary>
internal static class Scenarios
{
    public static async Task DigManyAsync(ServerRig rig, RigClient client, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var x = (i % 9) - 4f;
            var z = ((i / 9) % 5) - 2f;
            var reply = await rig.DigAsync(client, x, z);
            Assert.Equal(RejectReason.None, reply.Reason);
            rig.StepMany(20);
        }
    }

    public static async Task RunIntentsAsync(ServerRig rig, RigClient a, RigClient b)
    {
        for (var i = 0; i < 30; i++)
        {
            var client = i % 2 == 0 ? a : b;
            var x = (i % 9) - 4f;
            var z = ((i / 9) % 5) - 2f;
            var reply = await rig.DigAsync(client, x, z);
            Assert.Equal(RejectReason.None, reply.Reason);
            rig.StepMany(20);
        }
    }

    /// <summary>
    /// A server that dies: two players join, the intents are journaled and durable, then the writer is killed at the commit of the
    /// players' leave operations, which are lost like in a dead process.
    /// </summary>
    public static async Task<CrashedRun> CrashedRunAsync(TempDirectory dir, int intents)
    {
        var rig = new ServerRig(dir);
        await rig.StartAsync();
        var a = await rig.JoinAsync("local", 1);
        var b = await rig.JoinAsync("remote", 2);
        if (intents == 30)
        {
            await RunIntentsAsync(rig, a, b);
        }
        else
        {
            for (var i = 0; i < intents; i++)
            {
                var reply = await rig.DigAsync(i % 2 == 0 ? a : b, (i % 9) - 4f, 1.5f);
                Assert.Equal(RejectReason.None, reply.Reason);
                rig.StepMany(20);
            }
        }

        rig.StepMany(20);
        var expectedRows = rig.Runtime.State.LastOpId.Value;
        Assert.True(await rig.RunUntilAsync(() => RawDb.Long(dir.WorldPath, "SELECT COALESCE(MAX(op_id), 0) FROM journal") == (long)expectedRows));
        rig.Faults.CrashAt(FaultPoint.BeforeJournalCommit);
        a.Session.Disconnect();
        b.Session.Disconnect();
        Assert.True(await rig.RunUntilAsync(() => !rig.Runtime.State.Players.Online.Any()));

        // Let the flush timer fire and the writer die.
        rig.StepMany(20);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        return new CrashedRun(rig);
    }

    public sealed class CrashedRun(ServerRig rig) : IAsyncDisposable
    {
        public ServerRig Rig { get; } = rig;

        /// <summary>Stops the dead server's process objects; the crashed writer writes nothing any more.</summary>
        public async Task FinishCrashedRunAsync()
        {
            Rig.Runtime.RequestShutdown();
            for (var i = 0; i < 8000 && !Rig.Runtime.IsStopped; i++)
            {
                Rig.Step();
                await Task.Delay(1, TestContext.Current.CancellationToken);
            }
        }

        public ValueTask DisposeAsync() => Rig.DisposeAsync();
    }
}

using GdUnit4;

using Horyzont.Core.Hashing;
using Horyzont.Game.Tests.Support;
using Horyzont.Server;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class LifecycleTests
{
    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    public async Task FailureAfterRunning_GoesThroughCanonicalShutdown_WorldReloadsWithSameHash()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        string[] args = ["--solo", "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString()];

        ulong hashAfterStop;
        {
            // The READY line is printed after the server reached Running: the injected failure is a start failure after Running.
            var first = await ModeFixture.StartWithPrintHookAsync(
                scene, dir, true, line => ThrowOnReady(line), args);
            var composition = first.Composition;
            AssertThat(first.StartExit).IsNull();
            AssertThat(await first.WaitUntil(() => composition.IsFinished)).IsTrue();
            AssertThat(composition.ExitCode).IsEqual(Horyzont.Game.Boot.Boot.ExitFaulted);
            var steps = composition.Server!.CompletedShutdownSteps;
            AssertThat(steps.Any(s => s.Contains("checkpoint", StringComparison.Ordinal))).IsTrue();
            AssertThat(steps.Any(s => s.Contains("dispose writer", StringComparison.Ordinal))).IsTrue();
            AssertThat(steps.Any(s => s.Contains("terrain", StringComparison.Ordinal))).IsTrue();
            hashAfterStop = StateHasher.Hash(composition.Server.State);
            AssertThat(await first.StopAsync()).IsTrue();
        }

        var second = await ModeFixture.StartAsync(scene, dir, false, args);
        AssertThat(second.StartExit).IsNull();
        AssertThat(StateHasher.Hash(second.Composition.Server!.State)).IsEqual(hashAfterStop);
        AssertThat(await second.StopAsync()).IsTrue();
    }

    [TestCase(Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task LastWorldNotWritable_ServerKeepsRunning_AndStopsCleanly()
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        var port = ModeFixture.NextPort();
        var config = Path.Combine(dir, "server.json");
        File.WriteAllText(config, ModeFixture.SimpleConfig(port));

        // A directory in place of last-world.txt makes the write fail.
        Directory.CreateDirectory(Path.Combine(dir, "saves", "last-world.txt"));
        var server = await ModeFixture.StartAsync(scene, dir, false, "--server", "--config", config, "--data-dir", dir);

        AssertThat(server.StartExit).IsNull();
        AssertThat(server.Composition.Server!.RuntimeState).IsEqual(ServerRuntimeState.Running);
        AssertThat(server.Printed.Any(line => line.StartsWith("SERVER READY world=", StringComparison.Ordinal))).IsTrue();
        AssertThat(await server.StopAsync()).IsTrue();
        AssertThat(server.Composition.ExitCode).IsEqual(0);
    }

    [TestCase("--solo", Timeout = 300000)]
    [TestCase("--host", Timeout = 300000)]
    [RequireGodotRuntime]
    public async Task LocalClientLeaves_ServerShutsDownWithCheckpoint(string mode)
    {
        await using var scene = EngineTestScene.Create();
        var dir = ModeFixture.NewDirectory();
        List<string> args = [mode, "--data-dir", dir, "--profile", Path.Combine(dir, "profile.json"), "--world", Guid.NewGuid().ToString()];
        if (mode == "--host")
        {
            args.AddRange(["--bind", "127.0.0.1", "--port", ModeFixture.NextPort().ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }

        var fixture = await ModeFixture.StartAsync(scene, dir, true, [.. args]);
        AssertThat(await fixture.WaitInGame()).IsTrue();
        var composition = fixture.Composition;

        composition.Client!.Session.Disconnect();

        AssertThat(await fixture.WaitUntil(() => composition.IsFinished)).IsTrue();
        AssertThat(composition.Server!.RuntimeState).IsEqual(ServerRuntimeState.Stopped);
        AssertThat(composition.ExitCode).IsEqual(0);
        AssertThat(composition.Server.CompletedShutdownSteps.Any(s => s.Contains("checkpoint", StringComparison.Ordinal))).IsTrue();
        AssertThat(await fixture.StopAsync()).IsTrue();
    }

    private static void ThrowOnReady(string line)
    {
        if (line.StartsWith("SERVER READY", StringComparison.Ordinal))
        {
            throw new IOException("injected failure after the server reached Running");
        }
    }
}

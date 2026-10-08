using System.Diagnostics;
using System.Globalization;

using Horyzont.Core.Hashing;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

using Microsoft.Extensions.Time.Testing;

namespace Horyzont.Persistence.Tests;

/// <summary>Kills a real process that writes a world at a random moment; real time, nightly only.</summary>
public sealed class CrashHostSoakTests
{
    private const int Runs = 50;

    [Fact(Timeout = 20 * 60 * 1000)]
    [Trait("Category", "Soak")]
    public async Task KillAtRandomMoment_FiftyRuns_ReopenedWorldEqualsOracle()
    {
        var cancel = TestContext.Current.CancellationToken;
        var host = HostPath();
        var random = new Random(20261008);
        for (var run = 0; run < Runs; run++)
        {
            var seed = (ulong)(1000 + run);
            var killAfter = TimeSpan.FromMilliseconds(random.Next(50, 2001));
            using var dir = new TempDirectory();
            WorldCreator.Create(dir.WorldPath, WorldFactory.CreateDefault(), WorldHarness.Options, new FakeTimeProvider());

            var lastDurable = await RunAndKill(host, dir.WorldPath, seed, killAfter, cancel);

            var (_, restored, terrain) = WorldHarness.LoadAndReplay(dir.WorldPath);
            var (oracle, oracleTerrain) = WorldHarness.Oracle(seed, restored.State.LastOpId);
            Assert.True(
                restored.State.LastOpId.Value >= lastDurable,
                $"Run {run}: reported DURABLE {lastDurable}, restored only {restored.State.LastOpId.Value}.");
            Assert.Equal(StateHasher.Hash(oracle.State), StateHasher.Hash(restored.State));
            WorldHarness.AssertSameTerrain(oracle.State, oracleTerrain, terrain);
        }
    }

    private static async Task<ulong> RunAndKill(string host, string world, ulong seed, TimeSpan killAfter, CancellationToken cancel)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(world);
        start.ArgumentList.Add(seed.ToString(CultureInfo.InvariantCulture));
        var lastDurable = 0UL;
        var errors = new List<string>();
        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line && line.StartsWith("DURABLE ", StringComparison.Ordinal))
            {
                // Output events arrive one at a time; WaitForExitAsync waits for the last one.
                lastDurable = Math.Max(lastDurable, ulong.Parse(line.AsSpan(8), CultureInfo.InvariantCulture));
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (errors)
                {
                    errors.Add(line);
                }
            }
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await Task.Delay(killAfter, cancel);
        Assert.False(process.HasExited, $"Crash host exited early ({process.ExitCode}): {string.Join('\n', errors)}");
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancel).WaitAsync(TimeSpan.FromSeconds(30), cancel);
        return lastDurable;
    }

    private static string HostPath()
    {
        var testDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var path = Path.Combine(
            testDir.Replace("Horyzont.Persistence.Tests", "Horyzont.Persistence.CrashHost", StringComparison.Ordinal),
            "Horyzont.Persistence.CrashHost.dll");
        Assert.True(File.Exists(path), $"Crash host not built: {path}");
        return path;
    }
}

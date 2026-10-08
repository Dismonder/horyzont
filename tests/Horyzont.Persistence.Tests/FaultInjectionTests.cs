using FsCheck;
using FsCheck.Xunit;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests;

public sealed class FaultInjectionTests
{
    private const int CheckpointEvery = 20;

    /// <summary>
    /// Crash at any fault point after a random number of hits during a random intent sequence with a checkpoint every 20
    /// operations: the reopened world equals the oracle at its own <c>LastOpId</c>, which is not below the last durable operation.
    /// </summary>
    [Property(MaxTest = 40)]
    public async Task CrashAnywhere_ReloadAndReplay_EqualsOracleAndKeepsDurable(FaultPoint point, PositiveInt seed, PositiveInt hit, PositiveInt steps)
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.CrashAt(point, 1 + (hit.Get % 3));
        var scriptSeed = (ulong)seed.Get;
        var durableBefore = OpId.None;
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, faults))
        {
            var script = new WorldScript(scriptSeed);
            var nextCheckpoint = new OpId(CheckpointEvery);
            var count = 200 + (steps.Get % 400);
            for (var i = 0; i < count && !faults.Crashed; i++)
            {
                script.Step(h.Sim);
                h.Time.Advance(TimeSpan.FromMilliseconds(40));
                h.Pump();
                // Skipped while the previous one is in flight (after a crash it never completes).
                if (h.State.LastOpId >= nextCheckpoint && !h.State.IsCheckpointInFlight)
                {
                    // Awaiting keeps the writer from lagging behind the fast loop, so checkpoint fault points are reached.
                    await h.RequestCheckpoint();
                    h.Pump();
                    nextCheckpoint = new OpId(h.State.LastOpId.Value + CheckpointEvery);
                }

                durableBefore = h.Writer.DurableOpId;
            }

            await h.Writer.FlushAsync();
            h.Pump();
            durableBefore = h.Writer.DurableOpId;
        }

        var (_, restored, terrain) = WorldHarness.LoadAndReplay(dir.WorldPath);
        var (oracle, oracleTerrain) = WorldHarness.Oracle(scriptSeed, restored.State.LastOpId);

        Assert.True(restored.State.LastOpId >= durableBefore, $"Lost operations: durable {durableBefore.Value}, restored {restored.State.LastOpId.Value}.");
        Assert.Equal(StateHasher.Hash(oracle.State), StateHasher.Hash(restored.State));
        WorldHarness.AssertSameTerrain(oracle.State, oracleTerrain, terrain);
    }

    [Theory]
    [InlineData(FaultPoint.BeforeJournalCommit)]
    [InlineData(FaultPoint.AfterJournalCommit)]
    [InlineData(FaultPoint.CheckpointMidTransaction)]
    [InlineData(FaultPoint.BeforeCheckpointCommit)]
    [InlineData(FaultPoint.AfterCheckpointCommit)]
    public async Task CrashAtEveryPoint_IsReached(FaultPoint point)
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.CrashAt(point, 2);
        await using (var h = WorldHarness.CreateNew(dir.WorldPath, faults))
        {
            var script = new WorldScript(5);
            for (var round = 0; round < 3 && !faults.Crashed; round++)
            {
                for (var i = 0; i < 25; i++)
                {
                    script.Step(h.Sim);
                }

                await h.Writer.FlushAsync();
                h.Pump();
                if (!h.State.IsCheckpointInFlight)
                {
                    await h.RequestCheckpoint();
                }

                h.Pump();
            }

            Assert.True(faults.Crashed);
            Assert.False(await h.Writer.FlushAsync());
        }

        var (_, restored, terrain) = WorldHarness.LoadAndReplay(dir.WorldPath);
        var (oracle, oracleTerrain) = WorldHarness.Oracle(5, restored.State.LastOpId);
        Assert.Equal(StateHasher.Hash(oracle.State), StateHasher.Hash(restored.State));
        WorldHarness.AssertSameTerrain(oracle.State, oracleTerrain, terrain);
    }
}

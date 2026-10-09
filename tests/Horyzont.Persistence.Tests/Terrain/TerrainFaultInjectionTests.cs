using FsCheck;
using FsCheck.Xunit;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Persistence.Faults;
using Horyzont.Persistence.Terrain;
using Horyzont.Persistence.Tests.Support;
using Horyzont.Testing;

namespace Horyzont.Persistence.Tests.Terrain;

[Collection(SqlitePools.Name)]
public sealed class TerrainFaultInjectionTests
{
    private const int CheckpointEvery = 20;

    /// <summary>
    /// The terrain data of the fake is recorded in <see cref="ServerTerrainBlocks"/> and saved only through its checkpoint requests.
    /// Crash at any fault point: a new buffer over the reopened world serves, after replay, every edited block with the oracle's
    /// version and data at the restored <c>LastOpId</c>.
    /// </summary>
    [Property(MaxTest = 25)]
    public async Task CrashAnywhere_NewBufferAndReplay_ServesOracleBlocks(FaultPoint point, PositiveInt seed, PositiveInt hit, PositiveInt steps)
    {
        using var dir = new TempDirectory();
        var faults = ScriptedFaults.CrashAt(point, 1 + (hit.Get % 3));
        var scriptSeed = (ulong)seed.Get;
        var durableBefore = OpId.None;
        await using (var server = TerrainServer.CreateNew(dir.WorldPath, faults))
        {
            var script = new WorldScript(scriptSeed);
            var nextCheckpoint = new OpId(CheckpointEvery);
            var count = 150 + (steps.Get % 300);
            for (var i = 0; i < count && !faults.Crashed; i++)
            {
                script.Step(server.Sim);
                server.Time.Advance(TimeSpan.FromMilliseconds(40));
                server.Pump();
                if (server.State.LastOpId >= nextCheckpoint && !server.State.IsCheckpointInFlight)
                {
                    await server.RequestCheckpoint();
                    server.Pump();
                    nextCheckpoint = new OpId(server.State.LastOpId.Value + CheckpointEvery);
                }
            }

            await server.Writer.FlushAsync();
            server.Pump();
            durableBefore = server.Writer.DurableOpId;
        }

        await using var restored = TerrainServer.Open(dir.WorldPath);
        var (oracle, oracleTerrain) = WorldHarness.Oracle(scriptSeed, restored.State.LastOpId);

        Assert.True(restored.State.LastOpId >= durableBefore, $"Lost operations: durable {durableBefore.Value}, restored {restored.State.LastOpId.Value}.");
        Assert.Equal(StateHasher.Hash(oracle.State), StateHasher.Hash(restored.State));
        Assert.Null(restored.Blocks.FirstCorruptKey);
        foreach (var version in oracle.State.TerrainVersions.All)
        {
            Assert.Equal(TerrainBlockLookup.Found, restored.Blocks.TryLoad(version.Key, out var restoredVersion, out var blob));
            Assert.Equal(version.Version, restoredVersion);
            Assert.Equal(oracleTerrain.GetBlockData(version.Key), blob);
        }
    }
}

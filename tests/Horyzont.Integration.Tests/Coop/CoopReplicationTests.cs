using System.Numerics;

using Horyzont.Bots.Digests;
using Horyzont.Bots.Replicas;
using Horyzont.Bots.Runner;
using Horyzont.Bots.Scripting;
using Horyzont.Core.World.Terrain;
using Horyzont.Integration.Tests.Support;
using Horyzont.Net.Transport.InMemory;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// Phase B of K4-19: bots with the construct mirror (K3-15) and the terrain protocol (K3-16) against the entity and terrain replication
/// of the server; at every check break each bot's entries equal the server's and the builders hold everything in the shared zone.
/// </summary>
[Collection(SqlitePools.Name)]
public sealed class CoopReplicationTests
{
    private const float ZoneRadius = 100f;

    private static readonly BotRole[] _eight =
    [
        BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder,
        BotRole.Explorer, BotRole.Explorer, BotRole.Abuser,
    ];

    [Fact]
    public async Task TerrainReplica_ProtocolContract_MatchesServerReplicator()
    {
        // 20 s of edits, then 30 s of quiet in which the bot leaves and comes back with its cache.
        var schedule = new BotSchedule { ControlPeriodTicks = 3600, QuietTicks = 1800, DrainTicks = 600, BurstStartTicks = 100_000 };
        await using var rig = new CoopRig(replicated: true);
        await rig.StartServerAsync();
        var bot = rig.AddBot(BotRole.Builder, seed: 3, schedule);
        var replica = (BotReplicaSet)bot.Replica;

        await rig.RunAsync(1320);

        Assert.True(bot.IsInGame);
        Assert.True(replica.IsReady, "the startup transfer did not complete");
        Assert.True(replica.Terrain.ReportsSent + replica.Terrain.ViewsSent > 0);
        Assert.True(bot.Stats.Accepted > 5, "too few edits: " + bot.Stats.Accepted);
        Assert.Empty(CoopDigests.Differences(rig, coverage: true));
        var held = replica.Terrain.Held();
        Assert.Contains(held, h => h.Version > 0);
        Assert.Equal(0, replica.Terrain.Violations);

        // A bot that comes back reports its cache: the server sends none of the blocks it already holds.
        var received = replica.Terrain.BlocksReceived;
        bot.Leave();
        Assert.True(await rig.RunUntilAsync(() => bot.Stats.Reconnects >= 1 && bot.IsInGame && replica.IsReady, 1200));
        await rig.RunAsync(120);
        Assert.Equal(received, replica.Terrain.BlocksReceived);
        Assert.Equal(held.Count, replica.Terrain.Held().Count);
        Assert.Empty(CoopDigests.Differences(rig, coverage: true));
        await rig.StopServerAsync();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Coop_EightBots_TenMinutesVirtual_ConstructAndTerrainVersionDigestsEqual()
    {
        var schedule = new BotSchedule { ControlPeriodTicks = 2 * 60 * 60, QuietTicks = 20 * 60, DrainTicks = 10 * 60 };
        var checks = await RunWithChecksAsync(schedule, 10 * 60 * 60);

        Assert.Equal(5, checks);
    }

    [Fact]
    [Trait("Category", "Soak")]
    public async Task Coop_EightBots_TwoHoursVirtual_DigestsEqualAtEveryQuarter()
    {
        var checks = await RunWithChecksAsync(new BotSchedule(), 2 * 60 * 60 * 60);

        Assert.Equal(8, checks);
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Coop_Rtt100Jitter10_EditVisibleAtOthers_NetworkPartWithin150ms()
    {
        var conditions = new LinkConditions(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(10), LossRate: 0.05, DuplicateRate: 0, Seed: 19);
        await using var rig = new CoopRig(conditions: conditions, replicated: true);
        await rig.StartServerAsync();
        var tracker = new EditLatencyTracker(rig.Time);
        for (var i = 0; i < 5; i++)
        {
            tracker.Attach(rig.AddBot(BotRole.Builder, seed: 200UL + (ulong)i), i);
        }

        for (var minute = 0; minute < 3; minute++)
        {
            await rig.RunAsync(3600);
            tracker.Resolve();
        }

        var (count, p50, p99, max) = tracker.Summary();
        var line = string.Create(
            System.Globalization.CultureInfo.InvariantCulture, $"edit visibility at others: n={count} p50={p50:0.0} ms p99={p99:0.0} ms max={max:0.0} ms");
        TestContext.Current.SendDiagnosticMessage(line);
        if (Environment.GetEnvironmentVariable("HORYZONT_K4_REPORT") is { Length: > 0 } report)
        {
            // The acceptance report quotes the numbers (K4-19); CI may collect the file as an artefact.
            await File.AppendAllTextAsync(report, line + Environment.NewLine, TestContext.Current.CancellationToken);
        }
        Assert.True(count > 200, "too few samples: " + count);
        Assert.True(p99 <= 150.0, $"p99 {p99:0.0} ms (p50 {p50:0.0}, max {max:0.0}, n {count})");
        await rig.StopServerAsync();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task Coop_ExplorerLeavesAndReentersInterest_ReplicaMatchesAfterReturn()
    {
        // The explorer walks out for 200 s (beyond the 500 m interest), turns back and is home after 400 s; the break ends at 440 s.
        var schedule = new BotSchedule { ControlPeriodTicks = 26_400, QuietTicks = 1800, DrainTicks = 600, BurstStartTicks = 1_000_000 };
        await using var rig = new CoopRig(replicated: true);
        await rig.StartServerAsync();
        rig.AddBot(BotRole.Builder, seed: 31, schedule);
        rig.AddBot(BotRole.Builder, seed: 32, schedule);
        var explorer = rig.AddBot(BotRole.Explorer, seed: 33, schedule);

        await rig.RunAsync(BotScript.ExplorerLegTicks - 60);
        var far = Vector3.Distance(explorer.Own.Feet, Zone(rig));
        Assert.True(far > 600f, $"the explorer is only {far:0} m away");
        await rig.RunAsync((int)schedule.ControlPeriodTicks - (BotScript.ExplorerLegTicks - 60) - 1);

        Assert.True(Vector3.Distance(explorer.Own.Feet, Zone(rig)) < 300f);
        Assert.True(rig.Server.State.Constructs.Count > 0);
        Assert.Empty(CoopDigests.Differences(rig, coverage: true, coverageRoles: [BotRole.Builder, BotRole.Explorer]));
        await rig.StopServerAsync();
    }

    private static Vector3 Zone(CoopRig rig) => new(rig.Server.State.Planets.All[0].RadiusMeters, 0f, 0f);

    private static async Task<int> RunWithChecksAsync(BotSchedule schedule, int frames)
    {
        await using var rig = new CoopRig(replicated: true);
        await rig.StartServerAsync();
        for (var i = 0; i < _eight.Length; i++)
        {
            rig.AddBot(_eight[i], seed: 100UL + (ulong)i, schedule);
        }

        var checks = 0;
        var problems = new List<string>();
        for (var frame = 0; frame < frames; frame++)
        {
            rig.Step();
            if (frame % 300 == 299)
            {
                await rig.DrainWriterAsync();
            }

            if ((rig.Frame % schedule.ControlPeriodTicks) == 0)
            {
                checks++;
                problems.AddRange(CoopDigests.Differences(rig, coverage: true).Select(p => $"break {checks}: {p}"));
            }
        }

        Assert.All(rig.Bots, bot => Assert.Equal(0, bot.Stats.Disconnects));
        Assert.All(rig.Bots, bot => Assert.Equal(0, ((BotReplicaSet)bot.Replica).Terrain.Violations));
        Assert.True(rig.Server.State.Constructs.Count > 0, "no construct was built");
        Assert.True(rig.Server.State.TerrainVersions.Count > 0, "no terrain was edited");
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
        await rig.StopServerAsync();
        return checks;
    }
}

/// <summary>Compares the dumps of the bots of a replicated rig with the server, as <c>tools/coop/compare_dumps.py</c> does with files.</summary>
internal static class CoopDigests
{
    public static List<string> Differences(CoopRig rig, bool coverage, BotRole[]? coverageRoles = null)
    {
        var server = CoopServerDigest.Entries(rig.Server).ToDictionary(e => (e.Kind, e.Key));
        var planet = rig.Server.State.Planets.All[0];
        var zone = new Vector3(planet.RadiusMeters, 0f, 0f);
        var edge = TerrainMath.BlockSize * planet.VoxelSizeMeters;
        var roles = coverageRoles ?? [BotRole.Builder];
        var problems = new List<string>();
        foreach (var bot in rig.Bots)
        {
            var entries = ((BotReplicaSet)bot.Replica).Entries();
            foreach (var entry in entries)
            {
                if (!server.TryGetValue((entry.Kind, entry.Key), out var expected))
                {
                    // A block the server never edited is the generator block of version 0.
                    if (entry.Kind == DigestKind.Terrain && entry.Version == 0)
                    {
                        continue;
                    }

                    problems.Add($"{bot.Options.Name}: {entry.Kind} {entry.Key} v{entry.Version} is not on the server");
                }
                else if (expected.Version != entry.Version || expected.Hash != entry.Hash)
                {
                    problems.Add($"{bot.Options.Name}: {entry.Kind} {entry.Key} v{entry.Version}/{entry.Hash:x16}, server v{expected.Version}/{expected.Hash:x16}");
                }
            }

            if (!coverage || !roles.Contains(bot.Options.Role) || !bot.IsInGame)
            {
                continue;
            }

            var held = entries.Select(e => (e.Kind, e.Key)).ToHashSet();
            foreach (var construct in rig.Server.State.Constructs.All)
            {
                if (Vector3.Distance(construct.Origin, zone) <= ZoneRadius && !held.Contains((DigestKind.Construct, ConstructDigest.Entry(construct).Key)))
                {
                    problems.Add($"{bot.Options.Name}: construct {construct.Id.Value} in the zone is missing");
                }
            }

            // Terrain: only the builders stay close enough to hold the whole zone in their view (an explorer comes back to its home,
            // not to the centre of the zone).
            if (bot.Options.Role != BotRole.Builder)
            {
                continue;
            }

            foreach (var (key, _) in rig.Server.State.TerrainVersions.All)
            {
                var center = new Vector3((key.X + 0.5f) * edge, (key.Y + 0.5f) * edge, (key.Z + 0.5f) * edge);
                if (Vector3.Distance(center, zone) <= ZoneRadius && !held.Contains((DigestKind.Terrain, TerrainVersionDigest.FormatKey(key))))
                {
                    problems.Add($"{bot.Options.Name}: terrain block {TerrainVersionDigest.FormatKey(key)} in the zone is missing");
                }
            }
        }

        return problems;
    }

    private const float ZoneRadius = 100f;
}

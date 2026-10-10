using Horyzont.Bots.Client;
using Horyzont.Bots.Runner;
using Horyzont.Bots.Scripting;
using Horyzont.Core.Intents;
using Horyzont.Integration.Tests.Support;

namespace Horyzont.Integration.Tests.Coop;

[Collection(SqlitePools.Name)]
public sealed class CoopBotTests
{
    private const int OneMinute = 3600;

    [Fact]
    public async Task Coop_EightBots_OneMinuteVirtual_AllInGameAndEditsAccepted()
    {
        await using var rig = new CoopRig();
        await rig.StartServerAsync();
        var roles = new[]
        {
            BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder, BotRole.Builder,
            BotRole.Explorer, BotRole.Explorer, BotRole.Abuser,
        };
        for (var i = 0; i < roles.Length; i++)
        {
            rig.AddBot(roles[i], seed: 100UL + (ulong)i);
        }

        await rig.RunAsync(OneMinute);

        Assert.All(rig.Bots, bot => Assert.True(bot.IsInGame, bot.Options.Name + " is not in game"));
        Assert.Equal(8, rig.Server.Session.PlayersInGame.Count);
        Assert.All(rig.Bots, bot => Assert.Equal(0, bot.Stats.Disconnects));
        Assert.All(rig.Bots, bot => Assert.Equal(0, bot.Stats.UnknownReplies));
        foreach (var builder in rig.Bots.Where(b => b.Options.Role == BotRole.Builder))
        {
            var answered = builder.Stats.Accepted + builder.Stats.Rejected;
            Assert.True(answered > 80, builder.Options.Name + " sent too few intents: " + answered);
            var ratio = (double)builder.Stats.Accepted / answered;
            Assert.True(ratio >= 0.95, $"{builder.Options.Name}: {ratio:P1} accepted; rejections {Describe(builder.Stats)}");
        }

        // Every operation of the journal is accounted for: the joins and the accepted intents.
        var accepted = rig.Bots.Sum(b => b.Stats.Accepted);
        Assert.True(accepted > 0);
        Assert.Equal((ulong)(accepted + 8), rig.Server.State.LastOpId.Value);
        await rig.StopServerAsync();
    }

    [Fact]
    public async Task Coop_AbuserBot_RejectedWithReasonsAndNoStateChange()
    {
        await using var rig = new CoopRig();
        await rig.StartServerAsync();
        var abuser = rig.AddBot(BotRole.Abuser, seed: 9);

        await rig.RunAsync(OneMinute);

        Assert.True(abuser.IsInGame);
        var records = abuser.Stats.Records;
        Assert.NotEmpty(records);

        // What the server must refuse outright is always refused, and changes nothing.
        var refused = records.Where(r => r.Kind is BotAction.AbuseOutOfReach or BotAction.AbuseBadRadius).ToList();
        Assert.NotEmpty(refused);
        Assert.All(refused, r => Assert.NotEqual(RejectReason.None, r.Reason));
        Assert.All(refused, r => Assert.Contains(r.Reason, new[] { RejectReason.NoHit, RejectReason.InvalidParameters, RejectReason.OutOfReach }));
        Assert.All(refused, r => Assert.Equal(0UL, r.AppliedOpId.Value));

        // A non-finite direction never leaves the client.
        Assert.Equal(6 * 3, abuser.Stats.RefusedLocally);
        Assert.DoesNotContain(records, r => r.Kind == BotAction.AbuseNotFinite);

        // Too fast: the limiter lets through at most a burst of terrain edits per series and refuses the rest.
        var fast = records.Where(r => r.Kind == BotAction.AbuseTooFast).ToList();
        Assert.Equal(6 * 30, fast.Count);
        Assert.All(fast, r => Assert.Contains(r.Reason, new[] { RejectReason.None, RejectReason.RateLimited }));
        Assert.Contains(fast, r => r.Reason == RejectReason.RateLimited);
        Assert.InRange(fast.Count(r => r.Reason == RejectReason.None), 1, 6 * 8);

        // The journal holds the join and exactly the accepted too-fast edits.
        Assert.Equal((ulong)(1 + fast.Count(r => r.Reason == RejectReason.None)), rig.Server.State.LastOpId.Value);
        await rig.StopServerAsync();
    }

    [Fact]
    public async Task Crowd_PumpedOnVirtualTime_RunsEveryFrameOnceAndWritesTheReport()
    {
        await using var rig = new CoopRig();
        await rig.StartServerAsync();
        rig.AddBot(BotRole.Builder, seed: 1);
        rig.AddBot(BotRole.Abuser, seed: 2);
        var crowd = rig.UseCrowd();

        await rig.RunAsync(600);

        // The step that starts the clock ran frame 0; no frame ran twice or was skipped.
        Assert.InRange(crowd.LastFrame, 598, 600);
        Assert.Equal(0, crowd.SkippedFrames);
        Assert.All(rig.Bots, bot => Assert.True(bot.IsInGame));
        Assert.All(rig.Bots, bot => Assert.InRange(bot.Stats.Inputs, 590, 600));

        var path = Path.Combine(rig.DirectoryPath, "bots.json");
        BotReport.Write(path, rig.Bots, rig.Time, crowd.LastFrame + 1, crowd.SkippedFrames);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var bots = document.RootElement.GetProperty("bots");
        Assert.Equal(2, bots.GetArrayLength());
        Assert.Equal("Builder", bots[0].GetProperty("role").GetString());
        Assert.Equal(rig.Bots[0].Stats.Accepted, bots[0].GetProperty("accepted").GetInt32());
        Assert.Equal(rig.Bots[0].Stats.Records.Count, bots[0].GetProperty("intents").GetArrayLength());
        Assert.True(document.RootElement.GetProperty("frequency").GetInt64() > 0);
        Assert.False(File.Exists(path + ".tmp"));
    }

    private static string Describe(BotStats stats) =>
        string.Join(", ", stats.Records.Where(r => r.Reason != RejectReason.None).GroupBy(r => (r.Kind, r.Reason)).Select(g => $"{g.Key.Kind}/{g.Key.Reason}={g.Count()}"));
}

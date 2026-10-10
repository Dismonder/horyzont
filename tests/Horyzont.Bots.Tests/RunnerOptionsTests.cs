using Horyzont.Bots.Runner;
using Horyzont.Bots.Scripting;

namespace Horyzont.Bots.Tests;

public sealed class RunnerOptionsTests
{
    [Fact]
    public void Parse_FullLine_ReadsEverything()
    {
        var options = RunnerOptions.Parse(
        [
            "--connect", "127.0.0.1:27015", "--bots", "3", "--roles", "builder:2,abuser", "--seed", "9", "--epoch", "1760000000000",
            "--latency-ms", "50", "--jitter-ms", "10", "--minutes", "2.5", "--out", "x", "--identity-base", "42",
        ]);

        Assert.Equal("127.0.0.1", options.Host);
        Assert.Equal(27015, options.Port);
        Assert.Equal(3, options.Bots);
        Assert.Equal([BotRole.Builder, BotRole.Builder, BotRole.Abuser], options.Roles);
        Assert.Equal(BotRole.Builder, options.RoleOf(3));
        Assert.Equal(9UL, options.Seed);
        Assert.Equal(1760000000000L, options.EpochUnixMilliseconds);
        Assert.Equal(50, options.LatencyMilliseconds);
        Assert.Equal(2.5, options.Minutes);
        Assert.Equal(42UL, options.IdentityBase);
    }

    [Fact]
    public void Parse_Defaults_AreTheEightBotCrowd()
    {
        var options = RunnerOptions.Parse(["--connect", "10.0.0.2:1"]);

        Assert.Equal(8, options.Bots);
        Assert.Equal(5, Enumerable.Range(0, 8).Count(i => options.RoleOf(i) == BotRole.Builder));
        Assert.Equal(2, Enumerable.Range(0, 8).Count(i => options.RoleOf(i) == BotRole.Explorer));
        Assert.Equal(BotRole.Abuser, options.RoleOf(7));
    }

    [Theory]
    [InlineData("--bots", "3")]
    [InlineData("--connect", "localhost:5")]
    [InlineData("--connect", "1.2.3.4:0")]
    [InlineData("--connect", "1.2.3.4")]
    public void Parse_BadOrMissingConnect_Throws(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => RunnerOptions.Parse([name, value]));
    }

    [Theory]
    [InlineData("--bots", "0")]
    [InlineData("--bots", "65")]
    [InlineData("--roles", "wizard")]
    [InlineData("--roles", "builder:0")]
    [InlineData("--minutes", "-1")]
    [InlineData("--jitter-ms", "5")]
    [InlineData("--frobnicate", "1")]
    public void Parse_BadOption_Throws(string name, string value)
    {
        Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(["--connect", "127.0.0.1:27015", name, value]));
    }

    [Fact]
    public void Parse_OddNumberOfArguments_Throws()
    {
        Assert.Throws<ArgumentException>(() => RunnerOptions.Parse(["--connect", "127.0.0.1:1", "--bots"]));
    }
}

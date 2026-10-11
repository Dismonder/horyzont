using GdUnit4;

using Horyzont.Game.Boot;
using Horyzont.Game.Logging;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class LaunchOptionsTests
{
    [TestCase]
    public void Boot_BotWithoutDev_RejectedWithUsage()
    {
        AssertThrown(() => LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--bot", "5"]))
            .IsInstanceOf<ArgumentException>().HasMessage("--bot <seed> needs --connect <host:port> and --dev.");
        AssertThrown(() => LaunchOptions.Parse(["--solo", "--bot", "5"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--dev", "--bot-role", "builder"]))
            .IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--dev", "--bot", "5", "--bot-role", "pilot"]))
            .IsInstanceOf<ArgumentException>();
    }

    [TestCase]
    public void Parse_BotWithConnectAndDev_ReadsSeedRoleOutAndEpoch()
    {
        var options = LaunchOptions.Parse(
            ["--connect", "127.0.0.1:27015", "--dev", "--bot", "7", "--bot-role", "explorer", "--bot-out", "bots/e1", "--bot-epoch", "1760000000000"]);

        AssertThat(options.BotSeed).IsEqual(7UL);
        AssertThat(options.BotRole).IsEqual(Horyzont.Bots.Scripting.BotRole.Explorer);
        AssertThat(options.BotOut).IsEqual(Path.GetFullPath("bots/e1"));
        AssertThat(options.BotEpochUnixMilliseconds).IsEqual(1760000000000L);
        AssertThat(LaunchOptions.Parse(["--connect", "127.0.0.1:27015"]).BotSeed).IsNull();
    }

    [TestCase]
    public void Parse_SoloWithWorld_ReadsModeAndWorld()
    {
        var id = Guid.NewGuid();
        var options = LaunchOptions.Parse(["--solo", "--world", id.ToString()]);

        AssertThat(options.Mode).IsEqual(ProcessMode.Solo);
        AssertThat(options.WorldId).IsEqual(id);
    }

    [TestCase]
    public void Parse_HostWithPortAndPassword_ReadsThem()
    {
        var options = LaunchOptions.Parse(["--host", "--port", "28000", "--password", "secret", "--bind", "127.0.0.1"]);

        AssertThat(options.Mode).IsEqual(ProcessMode.Host);
        AssertThat(options.Port).IsEqual(28000);
        AssertThat(options.Password).IsEqual("secret");
        AssertThat(options.BindAddress).IsEqual("127.0.0.1");
    }

    [TestCase]
    public void Parse_DevToolsDemo_SoloAndHostOnly()
    {
        AssertThat(LaunchOptions.Parse(["--solo", "--dev-tools-demo"]).DevToolsDemo).IsTrue();
        AssertThat(LaunchOptions.Parse(["--solo"]).DevToolsDemo).IsFalse();
        AssertThrown(() => LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--dev-tools-demo"])).IsInstanceOf<ArgumentException>();
    }

    [TestCase]
    public void Parse_Connect_ReadsHostAndPort()
    {
        var options = LaunchOptions.Parse(["--connect", "192.168.1.5:27016"]);

        AssertThat(options.Mode).IsEqual(ProcessMode.Client);
        AssertThat(options.ConnectHost).IsEqual("192.168.1.5");
        AssertThat(options.ConnectPort).IsEqual(27016);
    }

    [TestCase]
    public void Parse_Server_NeedsConfig()
    {
        AssertThat(Throws(["--server"])).IsTrue();
        var options = LaunchOptions.Parse(["--server", "--config", "server.json", "--restore-latest-backup"]);

        AssertThat(options.Mode).IsEqual(ProcessMode.DedicatedServer);
        AssertThat(options.RestoreLatestBackup).IsTrue();
        AssertThat(Path.IsPathRooted(options.ConfigPath!)).IsTrue();
    }

    [TestCase]
    public void Parse_Dev_OnlyForTheDedicatedServerAndTheClient()
    {
        AssertThat(LaunchOptions.Parse(["--server", "--config", "server.json", "--dev"]).Dev).IsTrue();
        AssertThat(LaunchOptions.Parse(["--server", "--config", "server.json"]).Dev).IsFalse();
        AssertThat(LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--dev"]).Dev).IsTrue();
        AssertThat(Throws(["--solo", "--dev"])).IsTrue();
        AssertThat(Throws(["--host", "--dev"])).IsTrue();
    }

    [TestCase]
    public void Parse_WriteDefaultConfig_StandsAlone()
    {
        AssertThat(LaunchOptions.Parse(["--write-default-config", "x.json"]).WriteDefaultConfig).IsNotNull();
        AssertThat(Throws(["--write-default-config", "x.json", "--solo"])).IsTrue();
    }

    [TestCase]
    public void Parse_BadInput_Throws()
    {
        string[][] bad =
        [
            [],
            ["--solo", "--host"],
            ["--connect", "nohost"],
            ["--connect", "host:80"],
            ["--connect", ":27015"],
            ["--connect", "host:abc"],
            ["--host", "--port", "99999"],
            ["--host", "--port", "80"],
            ["--solo", "--world", "not-a-guid"],
            ["--solo", "--world", "00000000-0000-0000-0000-000000000000"],
            ["--solo", "--port", "27015"],
            ["--connect", "h:27015", "--world", "7ca5ea08-698c-453c-b7b0-1fa1323ee087"],
            ["--solo", "--bogus"],
            ["--solo", "stray"],
            ["--host", "--bind", "not-an-address"],
            ["--solo", "--password"],
            ["--solo", "--seed", "xyz"],
        ];
        foreach (var args in bad)
        {
            AssertThat(Throws(args)).IsTrue();
        }
    }

    [TestCase]
    public void IsModeRequested_OnlyForModeOptions()
    {
        AssertThat(LaunchOptions.IsModeRequested(["--solo"])).IsTrue();
        AssertThat(LaunchOptions.IsModeRequested(["--connect", "h:27015"])).IsTrue();
        AssertThat(LaunchOptions.IsModeRequested(["--smoke"])).IsFalse();
        AssertThat(LaunchOptions.IsModeRequested([])).IsFalse();
    }

    private static bool Throws(string[] args)
    {
        try
        {
            LaunchOptions.Parse(args);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }
}

[TestSuite]
public class LocalProfileTests
{
    [TestCase]
    public void LoadOrCreate_SecondCall_ReturnsTheSameIdentity()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"), "profile.json");

        var first = LocalProfile.LoadOrCreate(path);
        var second = LocalProfile.LoadOrCreate(path);

        AssertThat(first.Identity).IsEqual(second.Identity);
        AssertThat(first.Identity.Value).IsNotEqual(0UL);
        AssertThat(second.DisplayName).IsEqual(first.DisplayName);
    }

    [TestCase]
    public void LoadOrCreate_DamagedFile_IsReplaced()
    {
        var path = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"), "profile.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");

        var profile = LocalProfile.LoadOrCreate(path);

        AssertThat(profile.Identity.Value).IsNotEqual(0UL);
        AssertThat(LocalProfile.LoadOrCreate(path).Identity).IsEqual(profile.Identity);
    }
}

[TestSuite]
public class RotatingLogFileTests
{
    private static readonly string[] _expectedLogFiles = ["game.1.log", "game.2.log", "game.log"];

    [TestCase]
    public void WriteLine_PastLimit_RotatesAndKeepsOnlyTheNewestFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"));
        using (var file = new RotatingLogFile(dir, "game", 1024, 3))
        {
            var line = new string('x', 99);
            for (var i = 0; i < 100; i++)
            {
                file.WriteLine(line);
            }
        }

        var names = Directory.GetFiles(dir).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        AssertThat(names.SequenceEqual(_expectedLogFiles)).IsTrue();
        foreach (var name in names)
        {
            AssertThat(new FileInfo(Path.Combine(dir, name!)).Length <= 1124).IsTrue();
        }
    }

    [TestCase]
    public void Provider_WritesFileAndConsole_WarningsAsErrors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k2-10-tests", Guid.NewGuid().ToString("N"));
        var console = new List<(string Text, bool IsError)>();
        var file = new RotatingLogFile(dir, "game", 1024 * 1024, 2);
        using (var provider = new GodotLoggerProvider(file, LogLevel.Information, TimeProvider.System, (text, isError) => console.Add((text, isError))))
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.Log(LogLevel.Information, new EventId(7), "state", null, (s, _) => $"hello {s}");
            logger.Log(LogLevel.Debug, default, "state", null, (s, _) => "filtered out");
            logger.Log(LogLevel.Error, default, "state", new InvalidOperationException("boom"), (s, _) => "failed");
        }

        AssertThat(console.Count).IsEqual(2);
        AssertThat(console[0].IsError).IsFalse();
        AssertThat(console[0].Text.Contains("[Info] Test.Category #7: hello state", StringComparison.Ordinal)).IsTrue();
        AssertThat(console[1].IsError).IsTrue();
        var text = File.ReadAllText(Path.Combine(dir, "game.log"));
        AssertThat(text.Contains("hello state", StringComparison.Ordinal) && text.Contains("boom", StringComparison.Ordinal)).IsTrue();
        AssertThat(text.Contains("filtered out", StringComparison.Ordinal)).IsFalse();
    }

    [TestCase]
    public void Provider_AfterShutdown_KeepsTheEngineConsoleQuiet()
    {
        var dir = Path.Combine(Path.GetTempPath(), "horyzont-k4-19-tests", Guid.NewGuid().ToString("N"));
        var console = new List<string>();
        var fallback = new List<(string Text, bool IsError)>();
        var file = new RotatingLogFile(dir, "game", 1024 * 1024, 2);
        using (var provider = new GodotLoggerProvider(file, LogLevel.Information, TimeProvider.System, (text, _) => console.Add(text), (text, isError) => fallback.Add((text, isError))))
        {
            var logger = provider.CreateLogger("Test.Category");
            logger.Log(LogLevel.Information, default, "state", null, (s, _) => "before");
            Task.Run(() => logger.Log(LogLevel.Information, default, "state", null, (s, _) => "other thread")).Wait();
            provider.Shutdown();
            logger.Log(LogLevel.Information, default, "state", null, (s, _) => "quiet info");
            logger.Log(LogLevel.Error, default, "state", null, (s, _) => "loud error");
        }

        AssertThat(console.Count).IsEqual(1);
        AssertThat(fallback.Count).IsEqual(2);
        AssertThat(fallback[0].Text.Contains("other thread", StringComparison.Ordinal) && !fallback[0].IsError).IsTrue();
        AssertThat(fallback[1].Text.Contains("loud error", StringComparison.Ordinal) && fallback[1].IsError).IsTrue();
        var text = File.ReadAllText(Path.Combine(dir, "game.log"));
        AssertThat(text.Contains("quiet info", StringComparison.Ordinal)).IsTrue();
    }
}

using GdUnit4;

using Horyzont.Game.Boot;
using Horyzont.Server;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Boot;

[TestSuite]
public class LaunchOptionsTests
{
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

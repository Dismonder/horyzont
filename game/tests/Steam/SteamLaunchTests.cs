using GdUnit4;

using Horyzont.Client.Steam;
using Horyzont.Game.Boot;
using Horyzont.Server;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Steam;

[TestSuite]
public class SteamLaunchTests
{
    [TestCase]
    public void Launch_SteamStyleArgsWithoutSeparator_DetectsConnectLobby()
    {
        // OS.GetCmdlineArgs() as Steam starts the game: no "--", so Godot lists nothing as user arguments.
        string[] commandLine = ["horyzont.exe", "--path", "game", "+connect_lobby", "123", "--verbose"];

        var args = LaunchOptions.WithConnectLobby(commandLine, []);

        AssertThat(string.Join(",", args)).IsEqual("+connect_lobby,123");
        AssertThat(LaunchOptions.IsModeRequested(args)).IsTrue();
        var launch = LaunchOptions.Parse(args);
        AssertThat(launch.Mode).IsEqual(ProcessMode.Client);
        AssertThat(launch.ConnectLobbyId?.ToString()).IsEqual("123");
        AssertThat(launch.EnableSteam).IsTrue();
        AssertThat(launch.ConnectHost).IsNull();
    }

    [TestCase]
    public void Launch_SteamStyleArgs_TakesOnlyTheTokenAndItsValue()
    {
        string[] commandLine = ["horyzont.exe", "+connect_lobby", "123", "--solo", "--password", "x"];

        var args = LaunchOptions.WithConnectLobby(commandLine, ["--password", "pw"]);

        AssertThat(string.Join(",", args)).IsEqual("--password,pw,+connect_lobby,123");
    }

    [TestCase]
    public void Launch_ConnectLobbyAlreadyInUserArgs_IsNotDuplicated()
    {
        string[] commandLine = ["horyzont.exe", "--", "+connect_lobby", "5"];

        var args = LaunchOptions.WithConnectLobby(commandLine, ["+connect_lobby", "5"]);

        AssertThat(string.Join(",", args)).IsEqual("+connect_lobby,5");
    }

    [TestCase]
    public void Launch_NoConnectLobby_LeavesUserArgsAlone()
    {
        var args = LaunchOptions.WithConnectLobby(["horyzont.exe", "--", "--solo"], ["--solo"]);

        AssertThat(string.Join(",", args)).IsEqual("--solo");
        AssertThat(LaunchOptions.IsModeRequested(["+connect_lobby", "1"])).IsTrue();
    }

    [TestCase]
    public void Launch_ConnectLobbyArgument_UsesSameJoinFlow()
    {
        var launch = LaunchOptions.Parse(["+connect_lobby", "76561198000000001", "--password", "pw"]);

        AssertThat(launch.Mode).IsEqual(ProcessMode.Client);
        AssertThat(launch.ConnectLobbyId?.ToString()).IsEqual("76561198000000001");
        AssertThat(launch.Password).IsEqual("pw");
        AssertThat(launch.LobbyVisibility).IsEqual(LobbyVisibility.FriendsOnly);
        AssertThat(launch.SteamAppId).IsEqual(480u);
        AssertThat(launch.EnableSteam).IsTrue();
    }

    [TestCase]
    public void Launch_ConnectLobbyArgument_RejectsMissingZeroOverflowAndConflicts()
    {
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "--password", "x"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "0"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "-4"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "18446744073709551616"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "abc"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "1", "--solo"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "1", "--host"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "1", "--server", "--config", "c.json"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["+connect_lobby", "1", "--connect", "127.0.0.1:27015"])).IsInstanceOf<ArgumentException>();
    }

    [TestCase]
    public void Parse_HostWithSteamOptions_ReadsThem()
    {
        var launch = LaunchOptions.Parse(["--host", "--steam", "--lobby-visibility", "public", "--steam-app-id", "1234"]);

        AssertThat(launch.EnableSteam).IsTrue();
        AssertThat(launch.LobbyVisibility).IsEqual(LobbyVisibility.Public);
        AssertThat(launch.SteamAppId).IsEqual(1234u);
        AssertThat(LaunchOptions.Parse(["--host", "--lobby-visibility", "private"]).LobbyVisibility).IsEqual(LobbyVisibility.Private);
        AssertThat(LaunchOptions.Parse(["--host"]).EnableSteam).IsFalse();
    }

    [TestCase]
    public void Parse_BadSteamOptions_AreArgumentErrors()
    {
        AssertThrown(() => LaunchOptions.Parse(["--host", "--lobby-visibility", "everyone"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--host", "--steam-app-id", "0"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--host", "--steam-app-id", "4294967296"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--solo", "--steam"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--server", "--config", "c.json", "--steam"])).IsInstanceOf<ArgumentException>();
        AssertThrown(() => LaunchOptions.Parse(["--connect", "127.0.0.1:27015", "--lobby-visibility", "public"])).IsInstanceOf<ArgumentException>();
    }
}

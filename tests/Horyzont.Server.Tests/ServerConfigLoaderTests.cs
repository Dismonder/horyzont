using Horyzont.Server.Tests.Support;

namespace Horyzont.Server.Tests;

public sealed class ServerConfigLoaderTests
{
    [Theory]
    [InlineData("{ \"bogus\": 1 }", "bogus")]
    [InlineData("{ \"port\": 80 }", "port")]
    [InlineData("{ \"port\": 70000 }", "port")]
    [InlineData("{ \"maxPlayers\": 9 }", "maxPlayers")]
    [InlineData("{ \"maxPlayers\": 0 }", "maxPlayers")]
    [InlineData("{ \"checkpointIntervalSeconds\": 5 }", "checkpointIntervalSeconds")]
    [InlineData("{ \"backupIntervalHours\": 169 }", "backupIntervalHours")]
    [InlineData("{ \"backupsToKeep\": 0 }", "backupsToKeep")]
    [InlineData("{ \"port\": \"abc\" }", "port")]
    [InlineData("{ \"bindAddress\": \"not-an-address\" }", "bindAddress")]
    [InlineData("{ \"bindAddress\": null }", "bindAddress")]
    [InlineData("{ \"worldId\": \"00000000-0000-0000-0000-000000000000\" }", "worldId")]
    [InlineData("{ \"worldId\": \"nope\" }", "worldId")]
    [InlineData("{ \"password\": \"0123456789012345678901234567890123456789012345678901234567890123x\" }", "password")]
    [InlineData("{ \"logDirectory\": \"\" }", "logDirectory")]
    [InlineData("{ \"port\": 27015", "JSON")]
    [InlineData("null", "empty")]
    [InlineData("[]", "JSON")]
    public void TryLoad_InvalidValues_ReturnsFieldError(string json, string expectedInError)
    {
        using var dir = new TempDirectory();
        var path = dir.File("server.json");
        File.WriteAllText(path, json);

        var ok = ServerConfigLoader.TryLoad(path, out var config, out var error);

        Assert.False(ok);
        Assert.Contains(expectedInError, error, StringComparison.Ordinal);
        Assert.Equal(new ServerConfig(), config);
    }

    [Fact]
    public void TryLoad_FileOfSixtyFiveKiB_IsRefused()
    {
        using var dir = new TempDirectory();
        var path = dir.File("server.json");
        File.WriteAllText(path, "{ \"password\": \"" + new string('a', 65 * 1024) + "\" }");

        var ok = ServerConfigLoader.TryLoad(path, out _, out var error);

        Assert.False(ok);
        Assert.Contains("larger than", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoad_MissingFile_ReturnsError()
    {
        using var dir = new TempDirectory();

        var ok = ServerConfigLoader.TryLoad(dir.File("nothing.json"), out _, out var error);

        Assert.False(ok);
        Assert.Contains("does not exist", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoad_Defaults_MatchSpec()
    {
        using var dir = new TempDirectory();
        var path = dir.File("server.json");
        File.WriteAllText(path, "{}");

        var ok = ServerConfigLoader.TryLoad(path, out var config, out var error);

        Assert.True(ok, error);
        Assert.Equal(8, config.MaxPlayers);
        Assert.Equal(TimeSpan.FromSeconds(60), config.CheckpointInterval);
        Assert.Equal(6, config.BackupIntervalHours);
        Assert.Equal(10, config.BackupsToKeep);
        Assert.Equal(27015, config.Port);
        Assert.Null(config.WorldId);
        Assert.Equal("logs", config.LogDirectory);
    }

    [Fact]
    public void TryLoad_FullFileWithCommentsAndTrailingComma_MapsToOptions()
    {
        using var dir = new TempDirectory();
        var path = dir.File("server.json");
        File.WriteAllText(
            path,
            """
            {
              // the world to open
              "worldId": "6a1f5f2e-0000-4000-8000-000000000001",
              "bindAddress": "127.0.0.1",
              "port": 28000,
              "password": "secret",
              "maxPlayers": 4,
              "checkpointIntervalSeconds": 30,
              "backupIntervalHours": 2,
              "backupsToKeep": 3,
              "logDirectory": "var/logs",
            }
            """);

        var ok = ServerConfigLoader.TryLoad(path, out var config, out var error);

        Assert.True(ok, error);
        Assert.Equal(Guid.Parse("6a1f5f2e-0000-4000-8000-000000000001"), config.WorldId);
        var server = config.ToServerOptions(7);
        Assert.Equal((7UL, 4, "secret"), (server.BuildHash, server.MaxPlayers, server.Password));
        var persistence = config.ToPersistenceOptions("v");
        Assert.Equal((TimeSpan.FromHours(2), 3), (persistence.BackupInterval, persistence.BackupsToKeep));
        Assert.Equal(new Horyzont.Net.Transport.UdpEndpoint("127.0.0.1", 28000), config.ToListenEndpoint());
        Assert.Equal(TimeSpan.FromSeconds(30), config.CheckpointInterval);
    }

    [Fact]
    public void WriteDefault_ProducesAFileTheLoaderAccepts()
    {
        using var dir = new TempDirectory();
        var path = dir.File("nested/server.json");

        ServerConfigLoader.WriteDefault(path);
        var ok = ServerConfigLoader.TryLoad(path, out var config, out var error);

        Assert.True(ok, error);
        Assert.Equal(new ServerConfig(), config);
    }

    [Fact]
    public void WriteDefault_DoesNotWriteComputedProperties()
    {
        using var dir = new TempDirectory();
        var path = dir.File("server.json");

        ServerConfigLoader.WriteDefault(path);

        Assert.DoesNotContain("\"checkpointInterval\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
}

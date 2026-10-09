using Horyzont.Net.Session;
using Horyzont.Net.Transport;
using Horyzont.Persistence;

namespace Horyzont.Server;

/// <summary>
/// Settings of a dedicated server read from its JSON file (spec: server configuration). Nothing here changes the semantics of
/// operations or the <c>GameRules</c>, which are those of the build (lesson 1). Use <see cref="ServerConfigLoader.TryLoad"/> so the
/// ranges are checked.
/// </summary>
public sealed record ServerConfig
{
    // The properties have setters on purpose: the source-generated JSON deserializer fills init-only members from a synthetic
    // constructor, which would replace the defaults below by null for members missing in the file.

    /// <summary>Gets the world to open; null creates a new world.</summary>
    public Guid? WorldId { get; set; }

    /// <summary>Gets the IPv4 address to listen on.</summary>
    public string BindAddress { get; set; } = "0.0.0.0";

    /// <summary>Gets the UDP port, 1024..65535.</summary>
    public int Port { get; set; } = 27015;

    /// <summary>Gets the password clients must send, at most 64 bytes in UTF-8; empty means none (LAN only: UDP has no authentication yet).</summary>
    public string Password { get; set; } = "";

    /// <summary>Gets the player limit, 1..8.</summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>Gets the seconds between checkpoints, 10..3600.</summary>
    public int CheckpointIntervalSeconds { get; set; } = 60;

    /// <summary>Gets the hours between automatic backups, 1..168.</summary>
    public int BackupIntervalHours { get; set; } = 6;

    /// <summary>Gets the number of newest backups kept, 1..100.</summary>
    public int BackupsToKeep { get; set; } = 10;

    /// <summary>Gets the directory of the log files, relative to the data directory unless absolute.</summary>
    public string LogDirectory { get; set; } = "logs";

    /// <summary>Gets the interval between checkpoints.</summary>
    public TimeSpan CheckpointInterval => TimeSpan.FromSeconds(CheckpointIntervalSeconds);

    /// <summary>Builds the session options.</summary>
    /// <param name="buildHash">Hash of the build; clients of another build are refused.</param>
    /// <returns>The options.</returns>
    public ServerOptions ToServerOptions(ulong buildHash) =>
        new() { BuildHash = buildHash, MaxPlayers = MaxPlayers, Password = Password };

    /// <summary>Builds the writer options.</summary>
    /// <param name="gameVersion">Version of the game written to the world.</param>
    /// <returns>The options.</returns>
    public PersistenceOptions ToPersistenceOptions(string gameVersion) => new()
    {
        GameVersion = gameVersion,
        BackupInterval = TimeSpan.FromHours(BackupIntervalHours),
        BackupsToKeep = BackupsToKeep,
    };

    /// <summary>Gets the endpoint to listen on.</summary>
    /// <returns>The UDP endpoint.</returns>
    public UdpEndpoint ToListenEndpoint() => new(BindAddress, Port);
}

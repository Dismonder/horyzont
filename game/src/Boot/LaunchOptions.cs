using System.Globalization;

using Horyzont.Client.Steam;
using Horyzont.Server;

namespace Horyzont.Game.Boot;

/// <summary>
/// What the command line (user arguments after <c>--</c>) asks for: one process mode and its settings. Input is untrusted: every
/// value is validated and a bad one throws <see cref="ArgumentException"/> (exit code 2).
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>Default UDP port of a host and a dedicated server.</summary>
    public const int DefaultPort = 27015;

    private static readonly string[] _flags =
    [
        "--solo", "--host", "--server", "--restore-latest-backup", "--recover-from-checkpoint", "--dev", "--dev-tools-demo", "--steam",
    ];

    /// <summary>The Steam-style argument that carries a lobby id; it has no <c>--</c> separator and may stand outside the user arguments.</summary>
    public const string ConnectLobbyOption = "+connect_lobby";

    /// <summary>Steam application id used until the real one is configured (Spacewar).</summary>
    public const uint DefaultSteamAppId = 480;

    private static readonly string[] _valued =
    [
        "--connect", "--world", "--port", "--bind", "--password", "--config", "--data-dir", "--profile", "--seed", "--write-default-config",
        "--dev-speed", "--screenshot", "--screenshot-after", "--steam-app-id", "--lobby-visibility",
    ];

    /// <summary>Gets the mode of the process.</summary>
    public required ProcessMode Mode { get; init; }

    /// <summary>Gets the world to open or create (solo and host); null creates a new one.</summary>
    public Guid? WorldId { get; init; }

    /// <summary>Gets the UDP port a host listens on.</summary>
    public int Port { get; init; } = DefaultPort;

    /// <summary>Gets the address a host listens on.</summary>
    public string BindAddress { get; init; } = "0.0.0.0";

    /// <summary>Gets the password a host requires or a client sends.</summary>
    public string Password { get; init; } = "";

    /// <summary>Gets the host name or address a client connects to.</summary>
    public string? ConnectHost { get; init; }

    /// <summary>Gets the port a client connects to.</summary>
    public int ConnectPort { get; init; } = DefaultPort;

    /// <summary>Gets the server configuration file of a dedicated server.</summary>
    public string? ConfigPath { get; init; }

    /// <summary>Gets the directory that holds <c>saves/</c> (and the logs of a dedicated server); null means <c>user://</c> or, for a dedicated server, the executable directory.</summary>
    public string? DataDir { get; init; }

    /// <summary>Gets the profile file (identity and player name); null means <c>user://profile.json</c>.</summary>
    public string? ProfilePath { get; init; }

    /// <summary>Gets the galaxy seed of a new world.</summary>
    public ulong GalaxySeed { get; init; } = DevModeArguments.DefaultGalaxySeed;

    /// <summary>Gets a value indicating whether a corrupted world file is replaced by its newest backup before the start.</summary>
    public bool RestoreLatestBackup { get; init; }

    /// <summary>Gets a value indicating whether a journal tail written with other terrain semantics is dropped (owner decision).</summary>
    public bool RecoverFromCheckpoint { get; init; }

    /// <summary>Gets a value indicating whether development console commands (<c>dev-op</c>) are available; never set by players.</summary>
    public bool Dev { get; init; }

    /// <summary>Gets the factor applied to walking and sprinting speed in solo and host mode (<c>--dev-speed</c>, owner question P9); 1 normally.</summary>
    public float DevSpeed { get; init; } = 1f;

    /// <summary>Gets a value indicating whether the tools play a short script (dig, fill, place blocks) once the player aims at the ground (<c>--dev-tools-demo</c>, for screenshots).</summary>
    public bool DevToolsDemo { get; init; }

    /// <summary>Gets the file a screenshot of the game window is saved to (<c>--screenshot</c>); null for none.</summary>
    public string? ScreenshotPath { get; init; }

    /// <summary>Gets the seconds after the start when the screenshot is taken and the process quits (<c>--screenshot-after</c>).</summary>
    public float ScreenshotAfterSeconds { get; init; } = 20f;

    /// <summary>Gets the lobby a remote client joins at start (<c>+connect_lobby</c>); null when none.</summary>
    public ulong? ConnectLobbyId { get; init; }

    /// <summary>Gets a value indicating whether Steam lobbies and P2P are used (<c>--steam</c>, implied by <c>+connect_lobby</c>).</summary>
    public bool EnableSteam { get; init; }

    /// <summary>Gets the visibility of the lobby a Steam host creates.</summary>
    public LobbyVisibility LobbyVisibility { get; init; } = LobbyVisibility.FriendsOnly;

    /// <summary>Gets the Steam application id (<c>--steam-app-id</c>).</summary>
    public uint SteamAppId { get; init; } = DefaultSteamAppId;

    /// <summary>Gets the path to write a default server configuration to; the process then exits.</summary>
    public string? WriteDefaultConfig { get; init; }

    /// <summary>Tells whether the arguments pick a game mode (otherwise Boot prints the usage).</summary>
    /// <param name="args">User arguments.</param>
    /// <returns>True when one of the mode options is present.</returns>
    public static bool IsModeRequested(IReadOnlyCollection<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Contains("--solo") || args.Contains("--host") || args.Contains("--server") || args.Contains("--connect")
            || args.Contains("--write-default-config") || args.Contains(ConnectLobbyOption);
    }

    /// <summary>
    /// Adds the Steam launch pair <c>+connect_lobby &lt;id&gt;</c> from the full command line to the user arguments: Steam starts the
    /// game with it and without the <c>--</c> separator, so Godot does not list it among the user arguments. Only that token and its
    /// one value are taken over.
    /// </summary>
    /// <param name="commandLine">All arguments of the process (<c>OS.GetCmdlineArgs()</c>).</param>
    /// <param name="userArgs">User arguments after <c>--</c> (<c>OS.GetCmdlineUserArgs()</c>).</param>
    /// <returns>The user arguments, extended by the pair when it was found elsewhere.</returns>
    public static string[] WithConnectLobby(IReadOnlyList<string> commandLine, IReadOnlyList<string> userArgs)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        ArgumentNullException.ThrowIfNull(userArgs);
        var result = userArgs.ToList();
        if (result.Contains(ConnectLobbyOption))
        {
            return [.. result];
        }

        for (var i = 0; i < commandLine.Count; i++)
        {
            if (commandLine[i] == ConnectLobbyOption)
            {
                result.Add(ConnectLobbyOption);
                if (i + 1 < commandLine.Count)
                {
                    result.Add(commandLine[i + 1]);
                }

                break;
            }
        }

        return [.. result];
    }

    /// <summary>Parses and validates the arguments.</summary>
    /// <param name="args">User arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">Unknown or contradicting options or a bad value; the message names it.</exception>
    public static LaunchOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        CheckKnown(args);
        var reader = new DevModeArguments(args);
        var modes = new List<ProcessMode>();
        if (reader.Has("--solo"))
        {
            modes.Add(ProcessMode.Solo);
        }

        if (reader.Has("--host"))
        {
            modes.Add(ProcessMode.Host);
        }

        if (reader.Has("--server"))
        {
            modes.Add(ProcessMode.DedicatedServer);
        }

        var connect = reader.ValueOf("--connect");
        if (connect is not null)
        {
            modes.Add(ProcessMode.Client);
        }

        var connectLobby = ParseLobbyId(reader.ValueOf(ConnectLobbyOption));
        if (reader.Has(ConnectLobbyOption))
        {
            modes.Add(ProcessMode.Client);
        }

        var writeConfig = reader.ValueOf("--write-default-config");
        if (writeConfig is not null)
        {
            if (modes.Count != 0 || writeConfig.Length == 0 || writeConfig.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                throw new ArgumentException("--write-default-config <path> stands alone and needs a valid path.");
            }

            return new LaunchOptions { Mode = ProcessMode.DedicatedServer, WriteDefaultConfig = Path.GetFullPath(writeConfig) };
        }

        if (modes.Count != 1)
        {
            throw new ArgumentException("Exactly one of --solo, --host, --connect <host:port>, +connect_lobby <id>, --server is required.");
        }

        var mode = modes[0];
        var world = ParseWorld(reader.ValueOf("--world"));
        var port = ParsePort(reader.ValueOf("--port"), "--port") ?? DefaultPort;
        var bind = reader.ValueOf("--bind") ?? "0.0.0.0";
        if (!System.Net.IPAddress.TryParse(bind, out var bindAddress) || bindAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            throw new ArgumentException("--bind must be an IPv4 address.");
        }

        var password = reader.ValueOf("--password") ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(password) > 64)
        {
            throw new ArgumentException("--password has at most 64 bytes.");
        }

        var (host, connectPort) = connect is null ? (null, DefaultPort) : ParseEndpoint(connect);
        var config = reader.ValueOf("--config");
        RequireOnly(reader, "--world", mode, ProcessMode.Solo, ProcessMode.Host);
        RequireOnly(reader, "--port", mode, ProcessMode.Host);
        RequireOnly(reader, "--bind", mode, ProcessMode.Host);
        RequireOnly(reader, "--password", mode, ProcessMode.Host, ProcessMode.Client);
        RequireOnly(reader, "--config", mode, ProcessMode.DedicatedServer);
        RequireOnly(reader, "--data-dir", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.DedicatedServer);
        RequireOnly(reader, "--restore-latest-backup", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.DedicatedServer);
        RequireOnly(reader, "--recover-from-checkpoint", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.DedicatedServer);
        RequireOnly(reader, "--dev", mode, ProcessMode.DedicatedServer, ProcessMode.Client);
        RequireOnly(reader, "--seed", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.DedicatedServer);
        RequireOnly(reader, "--dev-speed", mode, ProcessMode.Solo, ProcessMode.Host);
        RequireOnly(reader, "--dev-tools-demo", mode, ProcessMode.Solo, ProcessMode.Host);
        RequireOnly(reader, "--screenshot", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.Client);
        RequireOnly(reader, "--screenshot-after", mode, ProcessMode.Solo, ProcessMode.Host, ProcessMode.Client);
        RequireOnly(reader, "--steam", mode, ProcessMode.Host, ProcessMode.Client);
        RequireOnly(reader, "--steam-app-id", mode, ProcessMode.Host, ProcessMode.Client);
        RequireOnly(reader, "--lobby-visibility", mode, ProcessMode.Host);
        var screenshot = reader.ValueOf("--screenshot");
        if (screenshot is not null && (screenshot.Length == 0 || screenshot.IndexOfAny(Path.GetInvalidPathChars()) >= 0))
        {
            throw new ArgumentException("--screenshot is not a valid path.");
        }
        if (mode == ProcessMode.DedicatedServer && config is null)
        {
            throw new ArgumentException("--server needs --config <server.json>.");
        }

        return new LaunchOptions
        {
            Mode = mode,
            WorldId = world,
            Port = port,
            BindAddress = bind,
            Password = password,
            ConnectHost = host,
            ConnectPort = connectPort,
            ConfigPath = config is null ? null : Path.GetFullPath(config),
            DataDir = reader.ValueOf("--data-dir") is { } dir ? Path.GetFullPath(dir) : null,
            ProfilePath = reader.ValueOf("--profile") is { } profile ? Path.GetFullPath(profile) : null,
            GalaxySeed = reader.GalaxySeed(),
            RestoreLatestBackup = reader.Has("--restore-latest-backup"),
            RecoverFromCheckpoint = reader.Has("--recover-from-checkpoint"),
            Dev = reader.Has("--dev"),
            DevSpeed = reader.Number("--dev-speed", 1f, 64f) ?? 1f,
            DevToolsDemo = reader.Has("--dev-tools-demo"),
            ScreenshotPath = screenshot is null ? null : Path.GetFullPath(screenshot),
            ScreenshotAfterSeconds = reader.Number("--screenshot-after", 1f, 3600f) ?? 20f,
            ConnectLobbyId = connectLobby,
            EnableSteam = reader.Has("--steam") || connectLobby is not null,
            LobbyVisibility = ParseVisibility(reader.ValueOf("--lobby-visibility")),
            SteamAppId = ParseAppId(reader.ValueOf("--steam-app-id")),
        };
    }

    private static void CheckKnown(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg == ConnectLobbyOption)
            {
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (i > 0 && (Array.IndexOf(_valued, args[i - 1]) >= 0 || args[i - 1] == ConnectLobbyOption))
                {
                    continue;
                }

                throw new ArgumentException($"Unexpected argument '{arg}'.");
            }

            if (Array.IndexOf(_flags, arg) < 0 && Array.IndexOf(_valued, arg) < 0)
            {
                throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }
    }

    private static void RequireOnly(DevModeArguments reader, string option, ProcessMode mode, params ProcessMode[] allowed)
    {
        if (reader.Has(option) && Array.IndexOf(allowed, mode) < 0)
        {
            throw new ArgumentException($"{option} does not apply to the mode {mode}.");
        }
    }

    private static ulong? ParseLobbyId(string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (!ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
        {
            throw new ArgumentException("+connect_lobby must be a non-zero lobby id (a 64-bit number).");
        }

        return id;
    }

    private static LobbyVisibility ParseVisibility(string? text) => text switch
    {
        null => LobbyVisibility.FriendsOnly,
        "private" => LobbyVisibility.Private,
        "friends" => LobbyVisibility.FriendsOnly,
        "public" => LobbyVisibility.Public,
        _ => throw new ArgumentException("--lobby-visibility must be private, friends or public."),
    };

    private static uint ParseAppId(string? text)
    {
        if (text is null)
        {
            return DefaultSteamAppId;
        }

        if (!uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
        {
            throw new ArgumentException("--steam-app-id must be a positive 32-bit number.");
        }

        return id;
    }

    private static Guid? ParseWorld(string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (!Guid.TryParse(text, out var id) || id == Guid.Empty)
        {
            throw new ArgumentException("--world must be a world id (a GUID).");
        }

        return id;
    }

    private static int? ParsePort(string? text, string option)
    {
        if (text is null)
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1024 or > 65535)
        {
            throw new ArgumentException($"{option} must be a port from 1024 to 65535.");
        }

        return port;
    }

    private static (string Host, int Port) ParseEndpoint(string text)
    {
        var colon = text.LastIndexOf(':');
        if (colon <= 0 || colon == text.Length - 1)
        {
            throw new ArgumentException("--connect must be <host>:<port>.");
        }

        var host = text[..colon];
        if (host.Length > 253 || host.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("--connect: the host is not valid.");
        }

        return (host, ParsePort(text[(colon + 1)..], "--connect port")!.Value);
    }
}

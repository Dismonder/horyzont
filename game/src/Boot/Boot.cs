using Godot;

using Horyzont.Client.Steam;
using Horyzont.Client.UI;
using Horyzont.Game.Logging;
using Horyzont.Game.UI;
using Horyzont.Server;

using Microsoft.Extensions.Logging;

using ServerProcessMode = Horyzont.Server.ProcessMode;

namespace Horyzont.Game.Boot;

/// <summary>Entry scene: picks the start mode from the user arguments after <c>--</c>.</summary>
public partial class Boot : Node
{
    /// <summary>Exit code for a failure that stopped the process before or during the start (a bug, not bad data).</summary>
    public const int ExitFaulted = 70;

    private const string Usage =
        "Horyzont modes (after --; without a mode the menu opens): --solo [--world <id>] [--dev-speed <1-64>] [--screenshot <png> [--screenshot-after <s>]] | --host [--world <id>] [--port 27015] [--bind <ipv4>] [--password <text>] | --connect <host:port> [--password <text>] [--dev [--bot <seed> [--bot-role builder|explorer|abuser] [--bot-out <dir>] [--bot-epoch <unix ms>]]]"
        + " | +connect_lobby <id> [--password <text>] (Steam) | --host/--connect [--steam] [--steam-app-id <id>] [--lobby-visibility private|friends|public]"
        + " | --server --config <server.json> [--data-dir <dir>] [--restore-latest-backup] [--dev] | --write-default-config <path>";

    private ILoggerFactory? _loggers;
    private GodotLoggerProvider? _provider;

    public override void _Ready()
    {
        // Steam starts the game with "+connect_lobby <id>" outside the user arguments; only that pair is taken over.
        var args = LaunchOptions.WithConnectLobby(OS.GetCmdlineArgs(), OS.GetCmdlineUserArgs());
        if (DevModes.TryRun(this, args))
        {
            return;
        }

        if (args.Contains("--smoke"))
        {
            RunSmoke();
            return;
        }

        if (args.Contains("--engine-tests"))
        {
            AddChild(Horyzont.Game.Testing.EngineTestRunner.FromArgs(args));
            return;
        }

        if (LaunchOptions.IsModeRequested(args))
        {
            _ = RunGameAsync(args);
            return;
        }

        if (DisplayServer.GetName() != "headless")
        {
            StartMenu(args);
            return;
        }

        // Headless and dedicated processes never create controls: without a mode there is nothing to do.
        GD.Print(Usage);
        GetTree().Quit(0);
    }

    public override void _ExitTree() => _loggers?.Dispose();

    private ILoggerFactory CreateLoggers(LaunchOptions launch, string userDir, string executableDir)
    {
        var dedicated = launch.Mode == ServerProcessMode.DedicatedServer;
        var dataDir = launch.DataDir ?? (dedicated ? executableDir : userDir);
        var logDir = Path.Combine(dataDir, "logs");
        if (dedicated && launch.ConfigPath is { } configPath && ServerConfigLoader.TryLoad(configPath, out var config, out _))
        {
            logDir = Path.GetFullPath(Path.Combine(dataDir, config.LogDirectory));
        }

        RotatingLogFile? file = null;
        try
        {
            file = new RotatingLogFile(logDir, dedicated ? "server" : "game", 5 * 1024 * 1024, 5);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // No log file (read-only disk, a locked file): the console still gets every entry, and the reason is shown once.
            GD.PrintErr($"Cannot open the log file in '{logDir}': {ex.Message}");
        }

        _provider = new GodotLoggerProvider(file, LogLevel.Information, TimeProvider.System);
        return LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information).AddProvider(_provider));
    }

    /// <summary>No mode on the command line: the menu decides. No world, transport or runtime exists until the player asks for one.</summary>
    private void StartMenu(string[] args)
    {
        var userDir = ProjectSettings.GlobalizePath("user://");
        var executableDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        _loggers = CreateLoggers(new LaunchOptions { Mode = ServerProcessMode.Solo }, userDir, executableDir);
        var env = new GameEnvironment
        {
            Time = TimeProvider.System,
            Loggers = _loggers,
            UserDataDir = userDir,
            ExecutableDir = executableDir,
            Render = true,
            DevBuild = OS.IsDebugBuild(),
        };
        IUiLobbyService? lobby = null;
        Func<bool>? platformShutdown = null;
        if (LaunchOptions.WantsMenuSteam(args, System.Environment.GetEnvironmentVariable("SteamAppId"), out var appId))
        {
            // X8: the services belong to the process, not to a session. The SDK is shut down by the UI coordinator at quit, after the last
            // session released its transports; the lifetime node keeps the callbacks running between sessions.
            var steam = GameComposition.CreatePlatformServices(env, appId);
            var lifetime = new SteamLifetime { Name = "SteamLifetime" };
            lifetime.Initialize(steam);
            AddChild(lifetime);
            env = env with { Platform = steam };
            lobby = new SteamUiLobbyService(steam.Lobby);
            platformShutdown = steam.TryShutdown;
        }

        var ui = new GameUiCoordinator { Name = "Ui" };
        ui.Initialize(env, lobby, platformShutdown);
        AddChild(ui);
    }

    private async Task RunGameAsync(string[] args)
    {
        LaunchOptions launch;
        try
        {
            launch = LaunchOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            GD.PrintErr($"ARGUMENT ERROR: {ex.Message}");
            GetTree().Quit(DevModes.ExitBadArguments);
            return;
        }

        if (launch.WriteDefaultConfig is { } target)
        {
            ServerConfigLoader.WriteDefault(target);
            GD.Print($"Wrote the default server configuration to {target}");
            GetTree().Quit(0);
            return;
        }

        var userDir = ProjectSettings.GlobalizePath("user://");
        var executableDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
        _loggers = CreateLoggers(launch, userDir, executableDir);
        var env = new GameEnvironment
        {
            Time = TimeProvider.System,
            Loggers = _loggers,
            UserDataDir = userDir,
            ExecutableDir = executableDir,
            Render = DisplayServer.GetName() != "headless",
            DevBuild = OS.IsDebugBuild(),
            ConsoleInput = launch.Mode == ServerProcessMode.DedicatedServer ? System.Console.In : null,
            ManageQuit = true,
            RegisterSignals = true,
            BeforeQuit = () => _provider?.Shutdown(),
        };

        if (launch.EnableSteam)
        {
            var steam = GameComposition.CreatePlatformServices(env, launch.SteamAppId);
            if (launch.ConnectLobbyId is not null && !steam.Available)
            {
                GD.PrintErr("STEAM UNAVAILABLE: +connect_lobby needs a running Steam client.");
                steam.TryShutdown();
                GetTree().Quit(ExitFaulted);
                return;
            }

            var lifetime = new SteamLifetime { Name = "SteamLifetime" };
            lifetime.Initialize(steam);
            AddChild(lifetime);
            env = env with { Platform = steam, ProcessCleanup = steam.TryShutdown };
        }

        GameComposition composition;
        try
        {
            composition = GameComposition.Create(launch, env);
        }
        catch (ArgumentException ex)
        {
            GD.PrintErr($"ARGUMENT ERROR: {ex.Message}");
            env.Platform?.TryShutdown();
            GetTree().Quit(DevModes.ExitBadArguments);
            return;
        }

        AddChild(composition.Root);
        try
        {
            if (await composition.StartAsync(CancellationToken.None) is { } exit)
            {
                // StartAsync released the transports of a world that did not start, so the lobby is left and the SDK may stop.
                env.Platform?.TryShutdown();
                GetTree().Quit(exit);
            }
        }
#pragma warning disable CA1031 // The entry point: any failure of the start ends the process with an error code instead of leaving a half started game.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            GD.PrintErr($"START FAILED: {ex}");
            composition.Release();
            _provider?.Shutdown();

            // Release disposed the transports and left the lobby (it throws otherwise, and the SDK then stays up).
            env.Platform?.TryShutdown();
            GetTree().Quit(ExitFaulted);
        }
    }

    private void RunSmoke()
    {
#if GODOT_REAL_T_IS_DOUBLE
        const string RealT = "double";
#else
        const string RealT = "float";
#endif
        var engine = (string)Engine.GetVersionInfo()["string"];
        GD.Print($"BOOT OK engine={engine} real_t={RealT} dotnet={System.Environment.Version}");
        GetTree().Quit(0);
    }
}

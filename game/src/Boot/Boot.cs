using Godot;

using Horyzont.Game.Logging;
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
        "Horyzont modes (after --): --solo [--world <id>] | --host [--world <id>] [--port 27015] [--bind <ipv4>] [--password <text>] | --connect <host:port> [--password <text>]"
        + " | --server --config <server.json> [--data-dir <dir>] [--restore-latest-backup] | --write-default-config <path>";

    private ILoggerFactory? _loggers;

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
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

        GD.Print(Usage);
        GetTree().Quit(0);
    }

    public override void _ExitTree() => _loggers?.Dispose();

    private static ILoggerFactory CreateLoggers(LaunchOptions launch, string userDir, string executableDir)
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

        return LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Information).AddProvider(new GodotLoggerProvider(file, LogLevel.Information, TimeProvider.System)));
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
            ConsoleInput = launch.Mode == ServerProcessMode.DedicatedServer ? System.Console.In : null,
            ManageQuit = true,
            RegisterSignals = true,
        };

        GameComposition composition;
        try
        {
            composition = GameComposition.Create(launch, env);
        }
        catch (ArgumentException ex)
        {
            GD.PrintErr($"ARGUMENT ERROR: {ex.Message}");
            GetTree().Quit(DevModes.ExitBadArguments);
            return;
        }

        AddChild(composition.Root);
        try
        {
            if (await composition.StartAsync(CancellationToken.None) is { } exit)
            {
                GetTree().Quit(exit);
            }
        }
#pragma warning disable CA1031 // The entry point: any failure of the start ends the process with an error code instead of leaving a half started game.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            GD.PrintErr($"START FAILED: {ex}");
            composition.Release();
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

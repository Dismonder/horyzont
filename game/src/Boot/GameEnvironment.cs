using Godot;

using Horyzont.Net.Transport.InMemory;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>What <see cref="GameComposition"/> needs from the process around it; tests replace the parts that touch the machine.</summary>
public sealed record GameEnvironment
{
    /// <summary>Gets the clock of the server, the sessions and the deadlines.</summary>
    public required TimeProvider Time { get; init; }

    /// <summary>Gets the logger factory (the Godot console and a rotating file in the game; usually a null factory in tests).</summary>
    public required ILoggerFactory Loggers { get; init; }

    /// <summary>Gets the absolute directory behind <c>user://</c> (saves, profile, logs of solo and host).</summary>
    public required string UserDataDir { get; init; }

    /// <summary>Gets the directory of the executable (default data directory of a dedicated server).</summary>
    public required string ExecutableDir { get; init; }

    /// <summary>Gets a value indicating whether terrain meshes are rendered; false without a rendering device (headless).</summary>
    public bool Render { get; init; }

    /// <summary>Gets the conditions (latency, loss) of the in-memory link between the server and the local client of a solo or host process; null for a perfect link. Tests of the prediction use it.</summary>
    public LinkConditions? LocalLink { get; init; }

    /// <summary>Gets a value indicating whether this is a development build: dev flight and the developer camera are available in solo and host mode.</summary>
    public bool DevBuild { get; init; }

    /// <summary>Gets a value indicating whether the characters (server bodies, local player, camera, sky) exist; the mode tests of K2-10 turn them off.</summary>
    public bool Characters { get; init; } = true;

    /// <summary>Gets the standard input a dedicated server reads its console from; null disables the console.</summary>
    public TextReader? ConsoleInput { get; init; }

    /// <summary>Gets the sink of lines for the operator (<c>SERVER READY</c>, console answers).</summary>
    public Action<string> Print { get; init; } = static line => GD.Print(line);

    /// <summary>Gets a value indicating whether the composition adds the <see cref="QuitCoordinator"/> (window close, signals, quit with the exit code).</summary>
    public bool ManageQuit { get; init; }

    /// <summary>Gets a value indicating whether the quit coordinator reacts to process signals.</summary>
    public bool RegisterSignals { get; init; }
}

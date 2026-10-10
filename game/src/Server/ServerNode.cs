using Godot;

using Horyzont.Server;

namespace Horyzont.Game.Server;

/// <summary>
/// <c>/Root/Server</c>: runs the server tick once per physics frame (60 Hz) on the main thread, before <c>/Root/Client</c> in the
/// same frame (priority -100), and the console of a dedicated server. Created by <c>GameComposition</c>, which calls
/// <see cref="Initialize"/> before the node enters the tree.
/// </summary>
public sealed partial class ServerNode : Node
{
    /// <summary>Process priority of the server node; the client node uses 0, so the tick always precedes the poll of a local client.</summary>
    public const int PhysicsPriority = -100;

    private ServerRuntime? _runtime;
    private DedicatedConsole? _console;
    private bool _consoleStopped;

    /// <summary>Gets the server runtime.</summary>
    public ServerRuntime Runtime => _runtime ?? throw new InvalidOperationException("ServerNode.Initialize was not called.");

    /// <summary>Gives the node its dependencies.</summary>
    /// <param name="runtime">The server runtime (not started yet; ticks before the start do nothing).</param>
    /// <param name="console">The console of a dedicated server; null otherwise.</param>
    public void Initialize(ServerRuntime runtime, DedicatedConsole? console)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
        _console = console;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        if (_runtime is null)
        {
            throw new InvalidOperationException("ServerNode.Initialize was not called before the node entered the tree.");
        }

        ProcessPhysicsPriority = PhysicsPriority;
        _console?.Start();
    }

    /// <inheritdoc />
    public override void _PhysicsProcess(double delta)
    {
        _console?.Pump();
        _runtime!.Tick();
        if (_runtime.IsStopped && !_consoleStopped)
        {
            _consoleStopped = true;
            _console?.Stop();
        }
    }
}

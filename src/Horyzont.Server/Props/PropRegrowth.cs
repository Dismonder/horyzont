using Horyzont.Core.Simulation;
using Horyzont.Core.World.Terrain;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Props;

/// <summary>
/// Regrowth of harvested flora as operations of the server (K6-31 D14): every <see cref="GameRules.PropRegrowScanEveryTicks"/> ticks it
/// looks at the harvested props from a cursor and regrows those whose time is up, whose ground was never dug and that have neither a player
/// nor a block within <see cref="GameRules.PropRegrowClearanceMeters"/>. Main thread only.
/// </summary>
public sealed class PropRegrowth : IServerTickParticipant
{
    private readonly Func<ServerSimulation> _simulation;
    private readonly Func<ITerrainService> _terrain;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger<PropRegrowth> _logger;

    /// <summary>Initializes a new instance of the <see cref="PropRegrowth"/> class.</summary>
    /// <param name="simulation">The simulation of the running world.</param>
    /// <param name="terrain">The terrain service the simulation samples; asked at the first scan.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown).</param>
    /// <param name="logger">Logger.</param>
    public PropRegrowth(Func<ServerSimulation> simulation, Func<ITerrainService> terrain, Func<bool> isReadOnly, ILogger<PropRegrowth> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _terrain = terrain;
        _isReadOnly = isReadOnly;
        _logger = logger;
    }

    /// <summary>Creates the schedule for a server and registers it as a tick participant; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="terrain">The terrain service of the simulation.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The schedule.</returns>
    public static PropRegrowth Register(ServerRuntime runtime, Func<ITerrainService> terrain, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(loggers);
        var regrowth = new PropRegrowth(
            () => runtime.Simulation, terrain, () => runtime.RuntimeState != ServerRuntimeState.Running, loggers.CreateLogger<PropRegrowth>());
        runtime.AddTickParticipant(regrowth);
        return regrowth;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Tick();
    }

    /// <summary>One tick of the schedule; scans on every <see cref="GameRules.PropRegrowScanEveryTicks"/>th tick.</summary>
    public void Tick()
    {
        // K6-31 C1: skeleton.
        _ = (_simulation, _terrain, _isReadOnly, _logger);
    }
}

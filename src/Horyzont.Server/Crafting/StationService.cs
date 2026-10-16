using Horyzont.Core.BlockEntities;
using Horyzont.Core.Simulation;
using Horyzont.Server.Inventory;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Crafting;

/// <summary>
/// Settles the stations with time that a player has open (K6-32 D16c): every <see cref="GameRules.StationSettleEveryTicks"/> ticks, for the stations
/// whose container somebody is subscribed to, the pieces done since the start move from the input to the output as a server operation, and the
/// subscribers get the deltas. A station nobody observes does not tick: its pieces are settled by the next interaction (opening it, moving items,
/// crafting at it, taking it down), and the world's ticks do not flow while the server is down. The settlement carries every value, so the result
/// does not depend on how often it runs (D15). Changes the world only through <see cref="ServerSimulation.TrySettleStation"/>, never from a handler of
/// <see cref="ServerSimulation.Applied"/>. Main thread only.
/// </summary>
public sealed class StationService : IServerTickParticipant
{
    private readonly Func<ServerSimulation?> _simulation;
    private readonly Action<List<BlockRef>> _collectObserved;
    private readonly Func<bool> _isReadOnly;
    private readonly ILogger<StationService> _logger;
    private readonly List<BlockRef> _observed = [];

    /// <summary>Initializes a new instance of the <see cref="StationService"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="collectObserved">Adds the stations with subscribers to the list, in <see cref="BlockRef"/> order.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown).</param>
    /// <param name="logger">Logger.</param>
    public StationService(Func<ServerSimulation?> simulation, Action<List<BlockRef>> collectObserved, Func<bool> isReadOnly, ILogger<StationService> logger)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(collectObserved);
        ArgumentNullException.ThrowIfNull(isReadOnly);
        ArgumentNullException.ThrowIfNull(logger);
        _simulation = simulation;
        _collectObserved = collectObserved;
        _isReadOnly = isReadOnly;
        _logger = logger;
    }

    /// <summary>Gets the number of settlements applied.</summary>
    public int SettlementsApplied { get; private set; }

    /// <summary>Creates the service for a server and registers it as a tick participant; call before the world starts.</summary>
    /// <param name="runtime">The server.</param>
    /// <param name="inventory">The inventory service that knows the subscriptions.</param>
    /// <param name="loggers">Loggers.</param>
    /// <returns>The service.</returns>
    public static StationService Register(ServerRuntime runtime, InventoryService inventory, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(loggers);
        var service = new StationService(
            () => runtime.RuntimeState is ServerRuntimeState.Running or ServerRuntimeState.SafeMode ? runtime.Simulation : null,
            inventory.CollectObservedStations,
            () => runtime.RuntimeState != ServerRuntimeState.Running,
            loggers.CreateLogger<StationService>());
        runtime.AddTickParticipant(service);
        return service;
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        Step();
    }

    /// <summary>Settles the observed stations when the tick of the simulation is a multiple of the settle interval.</summary>
    public void Step()
    {
        if (_simulation() is not { } sim || _isReadOnly() || sim.State.CurrentTick.Value % (ulong)sim.Rules.StationSettleEveryTicks != 0)
        {
            return;
        }

        _observed.Clear();
        _collectObserved(_observed);
        for (var i = 0; i < _observed.Count; i++)
        {
            if (sim.TrySettleStation(_observed[i]) is { } applied)
            {
                SettlementsApplied++;
                _logger.StationSettled(_observed[i].Construct.Value, applied.Id.Value);
            }
        }

        _observed.Clear();
    }
}

using Horyzont.Core.BlockEntities;
using Horyzont.Core.Simulation;
using Horyzont.Server.Inventory;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Crafting;

/// <summary>
/// Settles the stations with time that a player has open (K6-32 D16c): every <see cref="GameRules.StationSettleEveryTicks"/> ticks, for the stations
/// whose container somebody is subscribed to, the pieces done since the start move from the input to the output as a server operation. A station
/// nobody observes does not tick; its pieces are settled by the next interaction. Main thread only.
/// </summary>
public sealed class StationService : IServerTickParticipant
{
    /// <summary>Initializes a new instance of the <see cref="StationService"/> class.</summary>
    /// <param name="simulation">The simulation of the running world (null before the world has started).</param>
    /// <param name="collectObserved">Adds the stations with subscribers to the list, in <see cref="BlockRef"/> order.</param>
    /// <param name="isReadOnly">True when the server must not change the world (safe mode, shutdown).</param>
    /// <param name="logger">Logger.</param>
    public StationService(Func<ServerSimulation?> simulation, Action<List<BlockRef>> collectObserved, Func<bool> isReadOnly, ILogger<StationService> logger)
    {
        // K6-32 C3
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
        throw new NotImplementedException("K6-32 C3");
    }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        throw new NotImplementedException("K6-32 C3");
    }

    /// <summary>Settles the observed stations when the tick of the simulation is a multiple of the settle interval.</summary>
    public void Step()
    {
        throw new NotImplementedException("K6-32 C3");
    }
}

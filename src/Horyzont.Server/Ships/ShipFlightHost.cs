using Horyzont.Core.Ships.Flight;
using Horyzont.Core.Simulation;

using Microsoft.Extensions.Logging;

namespace Horyzont.Server.Ships;

/// <summary>
/// Registers the flight of ships before the world exists (K5-24 decision 10): phase <see cref="ServerTickPhase.Frames"/>, creates the
/// <see cref="ShipFlightSystem"/> on the first tick of a loaded world (and again for a new simulation) with the collider and the gate
/// made by the composition, the pilot at the controls and the launch guard (K5-25), wraps the pilot input in a
/// <see cref="DevControlSource"/> in development, then steps it every tick.
/// Console commands find it with <see cref="ServerRuntime.TryGetTickParticipant{T}"/>. Main thread.
/// </summary>
/// <param name="collider">Creates the hull collider of a running server.</param>
/// <param name="gate">Creates the terrain gate of a running server.</param>
/// <param name="dev">True with development pilots (<c>--dev</c>).</param>
/// <param name="tickRate">Server ticks per second.</param>
/// <param name="logger">Logger.</param>
/// <param name="rules">Flight rules; defaults when null.</param>
/// <param name="inputs">Pilot inputs of a running server (K5-25: the pilot at the controls and the launch guard); null = no pilots.</param>
public sealed class ShipFlightHost(
    Func<ServerRuntime, IShipCollider> collider, Func<ServerRuntime, IShipTerrainGate> gate, bool dev, int tickRate, ILogger logger,
    FlightRules? rules = null, Func<ServerRuntime, IShipInputSource>? inputs = null) : IServerTickParticipant
{
    private readonly Func<ServerRuntime, IShipCollider> _collider = collider ?? throw new ArgumentNullException(nameof(collider));
    private readonly Func<ServerRuntime, IShipTerrainGate> _gate = gate ?? throw new ArgumentNullException(nameof(gate));
    private readonly ILogger _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly FlightRules _rules = rules ?? FlightRules.Default;
    private ServerSimulation? _simulation;

    /// <inheritdoc />
    public ServerTickPhase Phase => ServerTickPhase.Frames;

    /// <summary>Gets the flight; null before the first tick of a world.</summary>
    public ShipFlightSystem? Flight { get; private set; }

    /// <summary>Gets the development pilots; null without <c>--dev</c> or before the first tick.</summary>
    public DevControlSource? Dev { get; private set; }

    /// <summary>Gets the pilot at the controls (K5-25); null without inputs or before the first tick.</summary>
    public PilotControlSource? Pilot { get; private set; }

    /// <inheritdoc />
    public void OnTick(ServerRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (Flight is null || !ReferenceEquals(_simulation, runtime.Simulation))
        {
            _simulation = runtime.Simulation;
            Flight = new ShipFlightSystem(
                runtime.Simulation, runtime.ShipStats, NoPilotControlSource.Instance, _collider(runtime), _gate(runtime), _rules, tickRate, _logger);
            Pilot = inputs is null ? null : new PilotControlSource(runtime.Simulation, runtime.ShipStats, inputs(runtime), _logger);
            IShipControlSource pilot = Pilot is null ? NoPilotControlSource.Instance : Pilot;
            Flight.Control = pilot;
            if (Pilot is not null)
            {
                Flight.LaunchGuard = new ShipLaunchGuard(runtime.Simulation, runtime.ShipStats, _logger);
            }

            Dev = dev ? new DevControlSource(Flight, runtime.State, pilot) : null;
            if (Dev is not null)
            {
                Flight.Control = Dev;
            }
        }

        Flight.Step();
    }
}

using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Ships.Flight;
using Horyzont.Core.World;

namespace Horyzont.Server.Ships;

/// <summary>
/// Development pilots (K5-24 decision 19, <c>--dev</c> only): a <see cref="DevTransitPilot"/> per ship flies it to a body
/// (<c>ship pilot &lt;id&gt; planet:&lt;id&gt;</c>); other ships get the wrapped source. The pilot ends when the ship parks on the
/// target. Positions of bodies come from the star system (the frames of the early access share their axes); without a system only
/// the home planet exists. Main thread.
/// </summary>
/// <param name="flight">The flight (views and ground probes).</param>
/// <param name="state">The world (planets).</param>
/// <param name="inner">The source for ships without a development pilot.</param>
public sealed class DevControlSource(ShipFlightSystem flight, WorldState state, IShipControlSource inner) : IShipControlSource
{
    /// <summary>Length of the ground probe under a piloted ship (metres).</summary>
    public const double GroundProbeMeters = 5000;

    private readonly ShipFlightSystem _flight = flight ?? throw new ArgumentNullException(nameof(flight));
    private readonly WorldState _state = state ?? throw new ArgumentNullException(nameof(state));
    private readonly IShipControlSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Dictionary<EntityId, Pilot> _pilots = [];

    /// <summary>Gets the number of piloted ships.</summary>
    public int Count => _pilots.Count;

    /// <summary>Starts a pilot to a planet.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="target">The target planet.</param>
    /// <returns>False for an unknown planet.</returns>
    public bool Start(EntityId ship, PlanetId target)
    {
        if (!_state.Planets.TryGet(target, out _))
        {
            return false;
        }

        _pilots[ship] = new Pilot(target, new DevTransitPilot());
        return true;
    }

    /// <summary>Stops the pilot of a ship (the ship autolands or follows its other source).</summary>
    /// <param name="ship">The ship.</param>
    /// <returns>True when a pilot was stopped.</returns>
    public bool Stop(EntityId ship) => _pilots.Remove(ship);

    /// <summary>The phase of the pilot of a ship.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="phase">Receives the phase.</param>
    /// <returns>False without a pilot.</returns>
    public bool TryGetPhase(EntityId ship, out DevTransitPhase phase)
    {
        phase = _pilots.TryGetValue(ship, out var pilot) ? pilot.Transit.Phase : default;
        return pilot is not null;
    }

    /// <inheritdoc />
    public bool TryGetControl(EntityId ship, Tick tick, out ShipControl control)
    {
        if (!_pilots.TryGetValue(ship, out var pilot) || !_flight.TryGetView(ship, out var view)
            || !TryBodyIn(view.Parent, pilot.Target, out var center, out var radius))
        {
            return _inner.TryGetControl(ship, tick, out control);
        }

        if (pilot.Obstacles is null)
        {
            pilot.Obstacles = new BodySphere[_state.Planets.All.Count];
        }

        var count = 0;
        foreach (var planet in _state.Planets.All)
        {
            if (count < pilot.Obstacles.Length && TryBodyIn(view.Parent, planet.Id, out var c, out var r))
            {
                pilot.Obstacles[count++] = new BodySphere(c, r);
            }
        }

        var ground = _flight.ProbeGround(ship, GroundProbeMeters);
        control = pilot.Transit.Compute(view.Motion, view.Stats, view.Environment, center, radius, ground, _flight.Rules, pilot.Obstacles.AsSpan(0, count));
        if (pilot.Transit.Phase == DevTransitPhase.Arrived)
        {
            _pilots.Remove(ship);
        }

        return true;
    }

    private bool TryBodyIn(FrameRef frame, PlanetId planet, out Vector3D center, out double radius)
    {
        center = default;
        radius = 0;
        if (!_state.Planets.TryGet(planet, out var parameters))
        {
            return false;
        }

        radius = parameters.RadiusMeters;
        var model = _flight.SystemModel();
        if (model is null || !model.TryGetBody(planet, out var body))
        {
            // No star system: only the frame of the planet itself places it.
            return frame.Kind == FrameKind.Planet && frame.Id == planet.Value;
        }

        center = body.Position;
        if (frame.Kind == FrameKind.Planet)
        {
            if (!model.TryGetBody(new PlanetId(frame.Id), out var parent))
            {
                return false;
            }

            center -= parent.Position;
        }

        return frame.Kind is FrameKind.Planet or FrameKind.System;
    }

    private sealed class Pilot(PlanetId target, DevTransitPilot transit)
    {
        public PlanetId Target { get; } = target;

        public DevTransitPilot Transit { get; } = transit;

        public BodySphere[]? Obstacles { get; set; }
    }
}

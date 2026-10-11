using Horyzont.Client.Session;
using Horyzont.Core.Frames;
using Horyzont.Core.Ids;
using Horyzont.Core.Protocol;
using Horyzont.Core.Protocol.Messages;
using Horyzont.Core.Serialization;
using Horyzont.Core.Space;
using Horyzont.Core.World;

namespace Horyzont.Client.Space;

/// <summary>
/// Planet placement of the client (K5-21 decision 15): before <see cref="StarSystemState"/> only the home planet (element 0 of the
/// hello) stands at the origin; after it every body stands in its pose. Fed by the session; main thread only.
/// </summary>
public sealed class ClientStarSystem : IClientSessionListener, IPlanetPlacement
{
    private IReadOnlyList<PlanetParams> _planets = [];

    /// <summary>Gets the home planet of the session; default before the first hello.</summary>
    public PlanetId HomePlanet { get; private set; }

    /// <summary>Gets the star system; null before <see cref="StarSystemState"/> or in a world without one.</summary>
    public StarSystemParams? System { get; private set; }

    /// <summary>Gets the model of the star system (zones, gravity); null while <see cref="System"/> is null.</summary>
    public StarSystemModel? Model { get; private set; }

    /// <summary>Gets the sun cycle: from the star system, otherwise the day length of the home planet; 0 before the first hello.</summary>
    public float SunCycleSeconds { get; private set; }

    /// <summary>Raised after a <see cref="StarSystemState"/> was applied.</summary>
    public event Action<StarSystemParams>? Changed;

    /// <inheritdoc />
    public void OnServerHello(ServerHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        _planets = hello.Planets;
        System = null;
        Model = null;
        HomePlanet = hello.Planets.Count > 0 ? hello.Planets[0].Id : default;
        SunCycleSeconds = hello.Planets.Count > 0 ? hello.Planets[0].DayLengthSeconds : 0;
    }

    /// <inheritdoc />
    public void OnMessage(Message message)
    {
        if (message is StarSystemState state)
        {
            Apply(state.System);
        }
    }

    /// <inheritdoc />
    public void OnDisconnected(DisconnectCode code)
    {
        System = null;
        Model = null;
    }

    /// <summary>Applies the star system of the server.</summary>
    /// <param name="system">The star system.</param>
    /// <exception cref="ProtocolViolationException">The system does not match the planets of the hello.</exception>
    public void Apply(StarSystemParams system)
    {
        ArgumentNullException.ThrowIfNull(system);
        if (system.HomePlanet != HomePlanet)
        {
            throw new ProtocolViolationException(
                $"Star system home planet {system.HomePlanet.Value} is not the home planet {HomePlanet.Value} of the hello.");
        }

        StarSystemModel model;
        try
        {
            model = new StarSystemModel(system, _planets);
        }
        catch (InvalidDataException ex)
        {
            throw new ProtocolViolationException(ex.Message);
        }

        System = system;
        Model = model;
        SunCycleSeconds = system.SunCycleSeconds;
        Changed?.Invoke(system);
    }

    /// <inheritdoc />
    public bool TryGetPose(PlanetId planet, out PoseD pose)
    {
        if (System is { } system)
        {
            var bodies = system.Bodies;
            for (var i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Planet == planet)
                {
                    pose = bodies[i].Pose;
                    return true;
                }
            }

            pose = default;
            return false;
        }

        pose = PoseD.Identity;
        return planet.Value != 0 && planet == HomePlanet;
    }
}

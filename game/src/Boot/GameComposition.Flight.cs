using Horyzont.Game.Frames;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Ships;
using Horyzont.Server;
using Horyzont.Server.Ships;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>
/// Flight of ships (K5-24): <see cref="ShipFlightHost"/> in phase Frames before the zone transitions (called from
/// <c>ConfigureSpace</c>, also on the dedicated server), the engine hull collider over the server construct world and the terrain gate
/// over the readiness of the planet terrains (K5-22); before the terrains exist, the home planet is the only ready one.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the flight of ships; null without a server.</summary>
    public ShipFlightHost? Flight { get; private set; }

    private void ConfigureFlight(ServerRuntime server)
    {
        var nodes = ServerFrameNodes;
        var constructs = Constructs;
        Flight = new ShipFlightHost(
            runtime => CreateShipCollider(runtime, nodes, constructs),
            runtime => CreateShipGate(runtime),
            _launch.Dev,
            GameServerHooks.TickRate,
            _env.Loggers.CreateLogger<ShipFlightHost>());
        server.AddTickParticipant(Flight);
    }

    private static GodotShipCollider CreateShipCollider(ServerRuntime runtime, FrameNodes? nodes, Constructs.HostConstructs? constructs)
    {
        var state = runtime.State;
        SharedWorldFrameSpaces? spaces = nodes is null
            ? null
            : new SharedWorldFrameSpaces(
                nodes,
                static () => null,
                id => state.Planets.TryGet(id, out var planet) ? planet : null,
                id => state.Constructs.TryGet(id, out var construct) ? construct.Mode : null);
        return new GodotShipCollider(
            state,
            runtime.ShipStats,
            frame => spaces is not null && spaces.TryGet(frame, out var space) ? space : null,
            (ship, into) =>
            {
                if (constructs is not null)
                {
                    GodotShipCollider.AddChunkBodies(constructs.World, ship, into);
                }
            });
    }

    private IShipTerrainGate CreateShipGate(ServerRuntime runtime)
    {
        if (ServerPlanets is { } planets)
        {
            return new PlanetTerrainShipGate(
                planets, runtime.State, Horyzont.Core.Ships.Flight.FlightRules.Default, PlanetGeneratorV1.MaxReliefMeters, GameServerHooks.TickRate,
                _env.Loggers.CreateLogger<PlanetTerrainShipGate>());
        }

        return new HomeOnlyGate(runtime.State.HomePlanet);
    }
}

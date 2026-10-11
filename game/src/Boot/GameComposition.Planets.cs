using Horyzont.Game.Server.Terrain;
using Horyzont.Server;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;

namespace Horyzont.Game.Boot;

/// <summary>
/// K5-22 wiring: the server terrains of the planets are activated on demand (<see cref="ServerPlanetTerrains"/>), in phase Compose after
/// the frame history and the frame nodes (README Y7). The terrain exists only once the world runs, so a small participant forwards
/// the tick to the planet terrains of the current server terrain and binds them to the frames on first use.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the server planet terrains of the running world (activation, leases, readiness); null without a running server terrain.</summary>
    public ServerPlanetTerrains? ServerPlanets => TerrainFactory?.Terrain?.Planets;

    partial void ConfigurePlanets()
    {
        if (Server is not { } server || TerrainFactory is not { } factory)
        {
            return;
        }

        server.AddTickParticipant(new PlanetTerrainsTicker(factory, ServerFrames, ServerFrameNodes));
    }

    private sealed class PlanetTerrainsTicker(
        Horyzont.Game.Server.VoxelServerTerrainFactory factory, ServerFramesBinder? frames, Horyzont.Game.Frames.FrameNodes? nodes) : IServerTickParticipant
    {
        private ServerPlanetTerrains? _bound;

        public ServerTickPhase Phase => ServerTickPhase.Compose;

        public void OnTick(ServerRuntime runtime)
        {
            if (factory.Terrain?.Planets is not { } planets)
            {
                return;
            }

            if (!ReferenceEquals(_bound, planets))
            {
                _bound = planets;
                planets.FrameNodes = nodes;
                planets.Graph = () => frames?.Graph;
            }

            if (planets.ShipDemand is null && frames?.History is { } history)
            {
                // K5-24 replaces this with UnparkedShipDemand (README Y11).
                planets.ShipDemand = new MovingShipDemand(history);
            }

            planets.OnTick(runtime);
        }
    }
}

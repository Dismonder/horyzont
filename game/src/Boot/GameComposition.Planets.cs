using System.Diagnostics;

using Horyzont.Game.Server.Terrain;
using Horyzont.Server;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;

using Microsoft.Extensions.Logging;

namespace Horyzont.Game.Boot;

/// <summary>
/// K5-22 wiring: the server terrains of the planets are activated on demand (<see cref="ServerPlanetTerrains"/>), in phase Compose after
/// the frame history and the frame nodes (README Y7). The terrain exists only once the world runs, so a small participant forwards
/// the tick to the planet terrains of the current server terrain and binds them to the frames on first use. The tick time of the
/// governor (decision 7) is measured from a marker in phase Frames (registered after the frame participants) to the planet terrains in
/// phase Compose — an approximation until the K4-19 <c>TickTimeRecorder</c> is on <c>main</c>.
/// </summary>
public sealed partial class GameComposition
{
    /// <summary>Gets the server planet terrains of the running world (activation, leases, readiness); null without a running server terrain.</summary>
    public ServerPlanetTerrains? ServerPlanets => TerrainFactory?.Terrain?.Planets;

    /// <summary>Gets the terrain tick governor of the server (decision 7); null without a server.</summary>
    public TerrainViewerThrottle? TerrainThrottle { get; private set; }

    partial void ConfigurePlanets()
    {
        if (Server is not { } server || TerrainFactory is not { } factory)
        {
            return;
        }

        var clock = new TickClock();
        TerrainThrottle = new TerrainViewerThrottle(
            new TerrainTickGovernor(GameServerHooks.TickRate, _env.Loggers.CreateLogger<TerrainTickGovernor>()));
        server.AddTickParticipant(clock);
        server.AddTickParticipant(new PlanetTerrainsTicker(factory, ServerFrames, ServerFrameNodes, clock, TerrainThrottle, () => _characters?.Characters));
    }

    private sealed class TickClock : IServerTickParticipant
    {
        private long _start;

        public ServerTickPhase Phase => ServerTickPhase.Frames;

        public double ElapsedMilliseconds => Stopwatch.GetElapsedTime(_start).TotalMilliseconds;

        public void OnTick(ServerRuntime runtime) => _start = Stopwatch.GetTimestamp();
    }

    private sealed class PlanetTerrainsTicker(
        Horyzont.Game.Server.VoxelServerTerrainFactory factory, ServerFramesBinder? frames, Horyzont.Game.Frames.FrameNodes? nodes, TickClock clock,
        TerrainViewerThrottle throttle, Func<Horyzont.Game.Server.Characters.ServerCharacters?> characters) : IServerTickParticipant
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
                planets.TickSeconds = 1.0 / GameServerHooks.TickRate;
            }

            if (planets.ShipDemand is null && frames?.History is { } history)
            {
                // K5-24 replaces this with UnparkedShipDemand (README Y11).
                planets.ShipDemand = new MovingShipDemand(history);
                planets.History = history;
            }

            planets.OnTick(runtime);
            throttle.Step(clock.ElapsedMilliseconds, runtime.State.CurrentTick.Value, runtime.State, characters(), planets);
        }
    }
}

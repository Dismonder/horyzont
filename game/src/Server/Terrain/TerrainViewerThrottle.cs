using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Server.Characters;
using Horyzont.Server.Space;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// Applies <see cref="TerrainTickGovernor"/> to the server terrains (K5-22 decision 7, spec 02:211): while throttled, the collision
/// viewer of a body alone on its planet stops following the body (<see cref="Node3D.TopLevel"/>) and is moved to it every
/// <see cref="TerrainTickGovernorOptions.ThrottledViewerIntervalTicks"/>-th tick; otherwise it follows the body again. While over budget,
/// one log entry per active terrain every <see cref="TerrainTickGovernorOptions.LogIntervalSeconds"/>. Bodies are reached through the
/// minimal hook <see cref="ServerCharacters.TryGetViewer"/> (ServerCharacters belongs to K5-20, README Y12). Main thread.
/// </summary>
public sealed class TerrainViewerThrottle
{
    private readonly TerrainTickGovernor _governor;
    private readonly Dictionary<ulong, int> _playersOnPlanet = [];

    /// <summary>Initializes a new instance of the <see cref="TerrainViewerThrottle"/> class.</summary>
    /// <param name="governor">The governor.</param>
    public TerrainViewerThrottle(TerrainTickGovernor governor)
    {
        ArgumentNullException.ThrowIfNull(governor);
        _governor = governor;
    }

    /// <summary>Gets the governor.</summary>
    public TerrainTickGovernor Governor => _governor;

    /// <summary>Records the tick time and applies the mode to the character viewers.</summary>
    /// <param name="tickMilliseconds">Duration of the tick.</param>
    /// <param name="tick">The tick.</param>
    /// <param name="state">The world.</param>
    /// <param name="characters">The server bodies; null without bodies.</param>
    /// <param name="planets">The planet terrains.</param>
    public void Step(double tickMilliseconds, ulong tick, WorldState state, ServerCharacters? characters, ServerPlanetTerrains planets)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(planets);
        _governor.Observe(tickMilliseconds);
        _playersOnPlanet.Clear();

        var players = state.Players.All;
        if (characters is not null)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (characters.TryGetState(players[i].Player, out var body))
                {
                    _playersOnPlanet[body.Planet.Value] = _playersOnPlanet.GetValueOrDefault(body.Planet.Value) + 1;
                }
            }

            for (var i = 0; i < players.Count; i++)
            {
                if (characters.TryGetState(players[i].Player, out var body) && characters.TryGetViewer(players[i].Player, out var viewer))
                {
                    Apply(viewer, _playersOnPlanet.GetValueOrDefault(body.Planet.Value), tick);
                }
            }
        }

        if (_governor.LogDue)
        {
            planets.LogTerrainStats(_governor, planet => _playersOnPlanet.GetValueOrDefault(planet.Value));
        }
    }

    private void Apply(VoxelViewer viewer, int playersOnPlanet, ulong tick)
    {
        var sparse = _governor.Throttled && playersOnPlanet == 1;
        if (!sparse)
        {
            if (viewer.TopLevel)
            {
                viewer.TopLevel = false;
                viewer.Position = Godot.Vector3.Zero;
            }

            return;
        }

        if (!viewer.TopLevel)
        {
            viewer.TopLevel = true;
            Follow(viewer);
        }
        else if (_governor.ShouldUpdateViewers(playersOnPlanet, tick))
        {
            Follow(viewer);
        }
    }

    private static void Follow(VoxelViewer viewer)
    {
        if (viewer.GetParent() is Node3D body && body.IsInsideTree())
        {
            viewer.GlobalPosition = body.GlobalPosition;
        }
    }
}

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Terrain;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// Collision viewers (no visuals) of ship-mode constructs in the frame of an active planet (K5-22 decision 5, README Y11): the ground
/// viewer stands in the <see cref="TerrainColumn"/> under the ship (<see cref="ShipTerrainColumns"/>), on the ground point of
/// <see cref="TerrainGround"/>; in the band near the terrain a second viewer stands at the <see cref="TerrainSweep"/> centre unless the
/// ground viewer is within <see cref="TerrainSweep.SingleViewerMeters"/> of it — the places the flight readiness query
/// (<see cref="PlanetTerrainReadinessQueries.GetShipReadiness"/>) checks. Position and range are refreshed every
/// <see cref="UpdateIntervalTicks"/> ticks; the viewer goes when the ship leaves the planet frame, stops being a ship or the planet is
/// deactivated. Viewers are children of a node outside the terrain nodes, placed through the global transform of the terrain node of
/// their planet. Main thread.
/// </summary>
public sealed class ServerShipViewers
{
    /// <summary>Ticks between refreshes of the viewers.</summary>
    public const int UpdateIntervalTicks = 10;

    private readonly Node3D _parent;
    private readonly SortedDictionary<ulong, Entry> _viewers = [];
    private readonly List<ulong> _gone = [];

    /// <summary>Initializes a new instance of the <see cref="ServerShipViewers"/> class.</summary>
    /// <param name="parent">Parent of the viewer nodes (in the scene tree).</param>
    public ServerShipViewers(Node3D parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent;
    }

    /// <summary>Gets the number of ship viewers.</summary>
    public int Count => _viewers.Count;

    /// <summary>Gets the number of sweep viewers (second viewers of ships in the band).</summary>
    public int SweepCount
    {
        get
        {
            var count = 0;
            foreach (var entry in _viewers.Values)
            {
                count += entry.Sweep is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <summary>Counts the ship viewers (ground and sweep) on a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The count.</returns>
    public int CountOn(PlanetId planet)
    {
        var count = 0;
        foreach (var entry in _viewers.Values)
        {
            count += entry.Planet != planet ? 0 : entry.Sweep is null ? 1 : 2;
        }

        return count;
    }

    /// <summary>The viewer of a ship.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="viewer">The viewer.</param>
    /// <returns>False when the ship has none.</returns>
    public bool TryGetViewer(EntityId ship, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VoxelViewer? viewer)
    {
        viewer = _viewers.TryGetValue(ship.Value, out var entry) ? entry.Viewer : null;
        return viewer is not null;
    }

    /// <summary>The sweep viewer of a ship.</summary>
    /// <param name="ship">The ship.</param>
    /// <param name="viewer">The viewer.</param>
    /// <returns>False when the ship has none (out of the band, or its ground viewer covers the sweep box).</returns>
    public bool TryGetSweepViewer(EntityId ship, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out VoxelViewer? viewer)
    {
        viewer = _viewers.TryGetValue(ship.Value, out var entry) ? entry.Sweep : null;
        return viewer is not null;
    }

    /// <summary>Refreshes the viewers of all ships (call every <see cref="UpdateIntervalTicks"/> ticks).</summary>
    /// <param name="state">The world.</param>
    /// <param name="history">The frame history (speed of the ships).</param>
    /// <param name="tickSeconds">Seconds per tick.</param>
    /// <param name="planets">The planet terrains (only active planets get viewers).</param>
    public void Update(WorldState state, ServerFrameHistory history, double tickSeconds, ServerPlanetTerrains planets)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(planets);
        foreach (var entry in _viewers.Values)
        {
            entry.Seen = false;
        }

        var constructs = state.Constructs.All;
        for (var i = 0; i < constructs.Count; i++)
        {
            var ship = constructs[i];
            if (ShipTerrainColumns.TryGetQuery(ship, state, history, tickSeconds, PlanetGeneratorV1.MaxReliefMeters, out var planet, out var query)
                && planets.TryGetNode(planet, out var node))
            {
                Apply(ship.Id, planet, query, node!, planets.TryGetGroundRadius(planet, query.Column.Center, out var ground) ? ground : float.NaN);
            }
        }

        RemoveWhere(static entry => !entry.Seen);
    }

    /// <summary>
    /// Places (creating when needed) the viewer of a ship in its column; the column is in the planet frame of <paramref name="node"/>.
    /// When the generated ground lies within the radial span of the column widened by the ground core (<see cref="TerrainGround"/>, the
    /// same test as the readiness query), the viewer stands on it (LOD 0, the collision LOD, is built
    /// only within <see cref="Horyzont.Game.Planet.PlanetTerrainFactory.LodDistance"/> of a viewer, the place the readiness query checks);
    /// otherwise at the centre of the column.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="planet">The planet of the column.</param>
    /// <param name="column">The column.</param>
    /// <param name="node">The terrain node of the planet (in the scene tree).</param>
    /// <param name="groundRadius">Radius of the generated ground under the column; NaN when unknown.</param>
    public void Apply(EntityId ship, PlanetId planet, in TerrainColumn column, PlanetTerrain node, float groundRadius)
        => Apply(ship, planet, new ShipTerrainQuery(column, default, false), node, groundRadius);

    /// <summary>
    /// Places the viewers of a ship for its query (README Y11): the ground viewer as for the column; in the band, a sweep viewer at the
    /// sweep centre unless the ground viewer is within <see cref="TerrainSweep.SingleViewerMeters"/> of it (removed otherwise).
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="planet">The planet of the query.</param>
    /// <param name="query">The query, in the planet frame of <paramref name="node"/>.</param>
    /// <param name="node">The terrain node of the planet (in the scene tree).</param>
    /// <param name="groundRadius">Radius of the generated ground under the column; NaN when unknown.</param>
    public void Apply(EntityId ship, PlanetId planet, in ShipTerrainQuery query, PlanetTerrain node, float groundRadius)
    {
        ArgumentNullException.ThrowIfNull(node);
        var column = query.Column;
        if (!_viewers.TryGetValue(ship.Value, out var entry) || !GodotObject.IsInstanceValid(entry.Viewer))
        {
            var viewer = new VoxelViewer { Name = $"ShipViewer_{ship.Value}", RequiresVisuals = false, RequiresCollisions = true };
            _parent.AddChild(viewer);
            entry = new Entry(viewer);
            _viewers[ship.Value] = entry;
        }

        entry.Planet = planet;
        entry.Seen = true;
        entry.Viewer.ViewDistance = (uint)MathF.Ceiling(column.ViewerDistance);
        var position = TerrainGround.ViewerPoint(column, groundRadius);
        entry.Viewer.GlobalPosition = node.GlobalTransform * TerrainVectors.ToGodot(position);
        if (!query.NeedsSweepViewer(position))
        {
            Free(entry.Sweep);
            entry.Sweep = null;
            return;
        }

        if (entry.Sweep is null || !GodotObject.IsInstanceValid(entry.Sweep))
        {
            entry.Sweep = new VoxelViewer { Name = $"ShipSweepViewer_{ship.Value}", RequiresVisuals = false, RequiresCollisions = true };
            _parent.AddChild(entry.Sweep);
        }

        // The data of the whole box must load too: its corner plus the margin of the column viewers.
        entry.Sweep.ViewDistance = (uint)MathF.Ceiling((query.Sweep.HalfSize * MathF.Sqrt(3f)) + TerrainColumn.ViewerMarginMeters);
        entry.Sweep.GlobalPosition = node.GlobalTransform * TerrainVectors.ToGodot(query.Sweep.Center);
    }

    /// <summary>Removes the viewers of a planet (before its terrain is deactivated).</summary>
    /// <param name="planet">The planet.</param>
    public void RemovePlanet(PlanetId planet) => RemoveWhere(entry => entry.Planet == planet);

    /// <summary>Removes every viewer (shutdown).</summary>
    public void Clear() => RemoveWhere(static _ => true);

    private void RemoveWhere(Func<Entry, bool> predicate)
    {
        _gone.Clear();
        foreach (var (id, entry) in _viewers)
        {
            if (predicate(entry))
            {
                _gone.Add(id);
            }
        }

        foreach (var id in _gone)
        {
            var entry = _viewers[id];
            _viewers.Remove(id);
            Free(entry.Viewer);
            Free(entry.Sweep);
        }
    }

    private static void Free(VoxelViewer? viewer)
    {
        if (viewer is not null && GodotObject.IsInstanceValid(viewer))
        {
            viewer.GetParent()?.RemoveChild(viewer);
            viewer.QueueFree();
        }
    }

    private sealed class Entry(VoxelViewer viewer)
    {
        public VoxelViewer Viewer { get; } = viewer;

        public PlanetId Planet { get; set; }

        public bool Seen { get; set; }

        public VoxelViewer? Sweep { get; set; }
    }
}

using Godot;

using Horyzont.Core.Ids;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Terrain;
using Horyzont.Server.Frames;
using Horyzont.Server.Space;

namespace Horyzont.Game.Server.Terrain;

/// <summary>
/// One collision viewer (no visuals) per ship-mode construct in the frame of an active planet (K5-22 decision 5, README Y11): it stands
/// in the <see cref="TerrainColumn"/> under the ship (<see cref="ShipTerrainColumns"/>), the same sphere the flight readiness query
/// covers, so the column is built before the ship reaches it. Position and range are refreshed every
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

    /// <summary>Counts the ship viewers on a planet.</summary>
    /// <param name="planet">The planet.</param>
    /// <returns>The count.</returns>
    public int CountOn(PlanetId planet)
    {
        var count = 0;
        foreach (var entry in _viewers.Values)
        {
            count += entry.Planet == planet ? 1 : 0;
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
            if (ShipTerrainColumns.TryGet(ship, state, history, tickSeconds, PlanetGeneratorV1.MaxReliefMeters, out var planet, out var column)
                && planets.TryGetNode(planet, out var node))
            {
                Apply(ship.Id, planet, column, node!, planets.TryGetGroundRadius(planet, column.Center, out var ground) ? ground : float.NaN);
            }
        }

        RemoveWhere(static entry => !entry.Seen);
    }

    /// <summary>
    /// Places (creating when needed) the viewer of a ship in its column; the column is in the planet frame of <paramref name="node"/>.
    /// When the generated ground lies within the radial span of the column, the viewer stands on it (LOD 0, the collision LOD, is built
    /// only within <see cref="Horyzont.Game.Planet.PlanetTerrainFactory.LodDistance"/> of a viewer, the place the readiness query checks);
    /// otherwise at the centre of the column.
    /// </summary>
    /// <param name="ship">The ship.</param>
    /// <param name="planet">The planet of the column.</param>
    /// <param name="column">The column.</param>
    /// <param name="node">The terrain node of the planet (in the scene tree).</param>
    /// <param name="groundRadius">Radius of the generated ground under the column; NaN when unknown.</param>
    public void Apply(EntityId ship, PlanetId planet, in TerrainColumn column, PlanetTerrain node, float groundRadius)
    {
        ArgumentNullException.ThrowIfNull(node);
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
        var distance = column.Center.Length();
        var position = column.Center;
        if (distance > 1f && float.IsFinite(groundRadius) && MathF.Abs(groundRadius - distance) <= column.Radius)
        {
            position = column.Center / distance * groundRadius;
        }

        entry.Viewer.GlobalPosition = node.GlobalTransform * TerrainVectors.ToGodot(position);
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
            var viewer = _viewers[id].Viewer;
            _viewers.Remove(id);
            if (GodotObject.IsInstanceValid(viewer))
            {
                viewer.GetParent()?.RemoveChild(viewer);
                viewer.QueueFree();
            }
        }
    }

    private sealed class Entry(VoxelViewer viewer)
    {
        public VoxelViewer Viewer { get; } = viewer;

        public PlanetId Planet { get; set; }

        public bool Seen { get; set; }
    }
}

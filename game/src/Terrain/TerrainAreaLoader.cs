using Godot;

using Horyzont.Core.Ids;

using Vector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Terrain;

/// <summary>
/// Loads terrain areas for the journal replay: a temporary <see cref="VoxelViewer"/> (no visuals, no collisions) stands at the
/// centre of the area until every LOD 0 block of it is editable, waiting engine frames up to a deadline. The viewer of a planet is
/// kept and moved for the next area, so the area stays loaded until the edit is applied; <see cref="ReleaseViewers"/> removes them.
/// Main thread.
/// </summary>
public sealed class TerrainAreaLoader
{
    /// <summary>Default deadline per area.</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(30);

    private const uint ViewerMarginMeters = 32;

    private readonly VoxelTerrainService _terrain;
    private readonly TimeProvider _time;
    private readonly TimeSpan _deadline;
    private readonly SortedDictionary<ulong, VoxelViewer> _viewers = [];

    /// <summary>Creates the loader.</summary>
    /// <param name="terrain">The terrain service with the planets.</param>
    /// <param name="time">Clock of the deadline.</param>
    /// <param name="deadline">Longest wait for one area.</param>
    public TerrainAreaLoader(VoxelTerrainService terrain, TimeProvider time, TimeSpan deadline)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentOutOfRangeException.ThrowIfLessThan(deadline, TimeSpan.Zero);
        _terrain = terrain;
        _time = time;
        _deadline = deadline;
    }

    /// <summary>Number of viewers this loader created (tests).</summary>
    public int ViewersCreated { get; private set; }

    /// <summary>Viewers currently placed.</summary>
    public int ActiveViewers => _viewers.Count;

    /// <summary>
    /// Returns when every LOD 0 block of the box <c>[center - radius, center + radius]</c> is editable; an already loaded area
    /// returns at once without a viewer.
    /// </summary>
    /// <param name="planet">Planet.</param>
    /// <param name="center">Area centre in the planet frame.</param>
    /// <param name="radius">Half edge of the area in metres (edit radius plus the margin).</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="InvalidOperationException">Unknown planet.</exception>
    /// <exception cref="TimeoutException">The area did not load within the deadline.</exception>
    public async Task EnsureLoadedAsync(PlanetId planet, Vector3 center, float radius, CancellationToken ct)
    {
        if (!_terrain.TryGetPlanet(planet, out var node))
        {
            throw new InvalidOperationException($"Planet {planet} has no terrain.");
        }

        if (_terrain.IsAreaEditable(planet, center, radius))
        {
            return;
        }

        if (!node.IsInsideTree())
        {
            throw new InvalidOperationException($"The terrain of planet {planet} is not in the scene tree.");
        }

        if (!_viewers.TryGetValue(planet.Value, out var viewer))
        {
            viewer = new VoxelViewer
            {
                Name = "ReplayViewer",
                RequiresVisuals = false,
                RequiresCollisions = false,
            };
            node.AddChild(viewer);
            _viewers.Add(planet.Value, viewer);
            ViewersCreated++;
        }

        viewer.ViewDistance = (uint)MathF.Ceiling(radius) + ViewerMarginMeters;
        viewer.Position = TerrainVectors.ToGodot(center);
        var started = _time.GetTimestamp();
        var tree = node.GetTree();
        while (!_terrain.IsAreaEditable(planet, center, radius))
        {
            ct.ThrowIfCancellationRequested();
            if (_time.GetElapsedTime(started) > _deadline)
            {
                ReleaseViewers();
                throw new TimeoutException(
                    $"Terrain area of planet {planet} around {center} (radius {radius} m) did not load within {_deadline.TotalSeconds} s.");
            }

            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Removes every viewer of this loader; the areas may unload afterwards.</summary>
    public void ReleaseViewers()
    {
        foreach (var viewer in _viewers.Values)
        {
            if (GodotObject.IsInstanceValid(viewer))
            {
                viewer.GetParent()?.RemoveChild(viewer);
                viewer.QueueFree();
            }
        }

        _viewers.Clear();
    }
}

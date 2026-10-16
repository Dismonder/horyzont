using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Core.Props;

/// <summary>
/// The props of the planets: the frozen layout of the generator profile placed on the generated surface (K6-31 D7). Used by the
/// validator, the resolvers and the regrowth schedule, never by the applier. Main thread only: it calls
/// <see cref="ITerrainService.TrySampleGeneratedSurface"/> and keeps an unsynchronised cache of anchors.
/// </summary>
public sealed class PropField
{
    private const int Slots = 4;
    private const double OffsetUnit = 1.0 / 1024.0;

    private readonly WorldState _state;
    private readonly ITerrainService _terrain;
    private readonly int _capacity;
    private readonly Dictionary<(PlanetId Planet, PropId Prop), LinkedListNode<CacheEntry>> _cache;
    private readonly LinkedList<CacheEntry> _recent = new();
    private readonly List<PropId> _cells = [];

    /// <summary>Creates the field.</summary>
    /// <param name="state">World state (planets, seeds).</param>
    /// <param name="terrain">Terrain service that samples the generated surface.</param>
    /// <param name="rules">Rules; the size of the anchor cache comes from them (<see cref="GameRules.PropAnchorCacheSize"/>), the default rules when null.</param>
    public PropField(WorldState state, ITerrainService terrain, GameRules? rules = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(terrain);
        _state = state;
        _terrain = terrain;
        _capacity = Math.Max(1, (rules ?? new GameRules()).PropAnchorCacheSize);
        _cache = new Dictionary<(PlanetId, PropId), LinkedListNode<CacheEntry>>(_capacity);
    }

    /// <summary>Looks up one prop.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="id">The prop.</param>
    /// <param name="instance">The prop when the layout has it, its height band accepts it and the surface can be sampled.</param>
    /// <returns>True when the prop exists.</returns>
    public bool TryGetProp(PlanetId planet, PropId id, out PropInstance instance)
    {
        if (_cache.TryGetValue((planet, id), out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            instance = node.Value.Instance;
            return node.Value.Present;
        }

        if (!_state.Planets.TryGet(planet, out var parameters) || !PropLayouts.TryGet(parameters.GeneratorProfile, out var layout)
            || !CubeSphereGrid.TryGetCellsPerFace(parameters.RadiusMeters, out var cellsPerFace))
        {
            instance = default;
            return false;
        }

        if (!id.IsValid || id.CellU >= cellsPerFace || id.CellV >= cellsPerFace || !layout.TryGetCandidate(parameters.Seed, id, out var candidate))
        {
            instance = default;
            return false;
        }

        var direction = CubeSphereGrid.DirectionOfPoint(
            id.Face, id.CellU + (candidate.OffsetU * OffsetUnit), id.CellV + (candidate.OffsetV * OffsetUnit), cellsPerFace);
        if (!_terrain.TrySampleGeneratedSurface(planet, direction, out var radius))
        {
            // No generated surface (unknown planet or no generator): nothing was sampled, so there is nothing to remember.
            instance = default;
            return false;
        }

        if (!float.IsFinite(radius) || !layout.Accepts(candidate.Kind, radius - parameters.RadiusMeters))
        {
            // The candidate exists but its height band refuses it. Remember the refusal, or a client could make the server sample the
            // surface again with every intent that names the same identifier (a refused hit does not use a token of the bucket).
            Remember((planet, id), default, present: false);
            instance = default;
            return false;
        }

        instance = new PropInstance(id, candidate.Kind, direction * radius, direction, candidate.Scale / 1000f);
        Remember((planet, id), instance, present: true);
        return true;
    }

    /// <summary>Appends the props whose anchor lies in a sphere, ascending by <see cref="PropId"/>.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="center">Centre of the sphere in the planet frame.</param>
    /// <param name="radius">Radius in metres.</param>
    /// <param name="result">The list to append to.</param>
    public void CollectInSphere(PlanetId planet, Vector3 center, float radius, List<PropInstance> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!_state.Planets.TryGet(planet, out var parameters) || !PropLayouts.TryGet(parameters.GeneratorProfile, out var layout)
            || !CubeSphereGrid.TryGetCellsPerFace(parameters.RadiusMeters, out var cellsPerFace) || !float.IsFinite(radius) || radius < 0f
            || !float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z))
        {
            return;
        }

        // An anchor lies up to a cell diagonal from the centre of its cell, and the cells of a face shrink towards its corners: 1.5 cell
        // widths of margin around the sphere reach every cell that can hold an anchor inside it.
        var cellMeters = parameters.RadiusMeters * MathF.PI / 2f / cellsPerFace;
        _cells.Clear();
        CubeSphereGrid.CellsInSphere(center, radius + (1.5f * cellMeters), parameters.RadiusMeters, _cells);
        var radiusSquared = radius * radius;
        foreach (var cell in _cells)
        {
            for (var slot = 0; slot < Slots; slot++)
            {
                var id = new PropId(cell.Value | ((ulong)slot << 16));
                if (RayMissesSphere(parameters, layout, cellsPerFace, id, center, radiusSquared))
                {
                    continue;
                }

                if (TryGetProp(planet, id, out var instance) && Vector3.DistanceSquared(instance.Anchor, center) <= radiusSquared)
                {
                    result.Add(instance);
                }
            }
        }
    }

    /// <summary>
    /// True when the candidate of a prop certainly has no anchor in the sphere: the anchor lies on the ray from the centre of the planet
    /// along the direction of the candidate, so a ray that passes farther than the radius from the centre of the sphere cannot meet it.
    /// Costs a hash and some arithmetic, no surface sampling. A cell without a candidate counts as a miss.
    /// </summary>
    private static bool RayMissesSphere(PlanetParams parameters, IPropLayout layout, int cellsPerFace, PropId id, Vector3 center, float radiusSquared)
    {
        if (!layout.TryGetCandidate(parameters.Seed, id, out var candidate))
        {
            return true;
        }

        var direction = CubeSphereGrid.DirectionOfPoint(
            id.Face, id.CellU + (candidate.OffsetU * OffsetUnit), id.CellV + (candidate.OffsetV * OffsetUnit), cellsPerFace);
        var along = Vector3.Dot(center, direction);
        var perpendicular = center - (direction * along);

        // A margin of centimetres covers the single precision error of the direction.
        const float epsilon = 0.01f;
        var limit = MathF.Sqrt(radiusSquared) + epsilon;
        return perpendicular.LengthSquared() > limit * limit;
    }

    private void Remember((PlanetId Planet, PropId Prop) key, PropInstance instance, bool present)
    {
        if (_cache.Count >= _capacity && _recent.Last is { } oldest)
        {
            // Full: the least recently used entry gives its node to the new one, so a steady state allocates nothing.
            _cache.Remove((oldest.Value.Planet, oldest.Value.Prop));
            _recent.RemoveLast();
            oldest.Value = new CacheEntry(key.Planet, key.Prop, present, instance);
            _recent.AddFirst(oldest);
            _cache[key] = oldest;
            return;
        }

        _cache[key] = _recent.AddFirst(new CacheEntry(key.Planet, key.Prop, present, instance));
    }

    private readonly record struct CacheEntry(PlanetId Planet, PropId Prop, bool Present, PropInstance Instance);
}

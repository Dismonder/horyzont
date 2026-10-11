using System.Numerics;

namespace Horyzont.Server.Space;

/// <summary>
/// A small cache of a radius per direction (the generated ground under a query, K5-22 readiness and ship viewers): the last
/// <see cref="Capacity"/> directions, reused within <see cref="SameDirectionRadians"/>, replaced oldest first. Several ships and the column
/// and sweep queries of each alternate without evicting one another (Opus review of K5-22). The generated ground never changes, so
/// entries never go stale. Allocation-free after construction; one thread.
/// </summary>
public sealed class DirectionRadiusCache
{
    /// <summary>Entries kept.</summary>
    public const int Capacity = 16;

    /// <summary>
    /// Angle under which a cached radius is reused (about 0.5 m at R = 4 km). Compared as the squared chord between unit vectors: a
    /// cosine this close to 1 is not representable in single precision (the former 0.99999998f was 1.0f, so the cache almost never hit).
    /// </summary>
    public const float SameDirectionRadians = 1.25e-4f;

    private readonly Vector3[] _directions = new Vector3[Capacity];
    private readonly float[] _radii = new float[Capacity];
    private int _count;
    private int _next;

    /// <summary>Gets the number of computations made (cache misses).</summary>
    public long Misses { get; private set; }

    /// <summary>The radius along a direction, computed by <paramref name="compute"/> on a miss.</summary>
    /// <param name="direction">The direction (any length; zero means +Y).</param>
    /// <param name="compute">Computes the radius along a unit direction.</param>
    /// <returns>The radius.</returns>
    public float Get(Vector3 direction, Func<Vector3, float> compute)
    {
        ArgumentNullException.ThrowIfNull(compute);
        var length = direction.Length();
        var unit = length > 1e-6f && float.IsFinite(length) ? direction / length : Vector3.UnitY;
        for (var i = 0; i < _count; i++)
        {
            if (Vector3.DistanceSquared(unit, _directions[i]) <= SameDirectionRadians * SameDirectionRadians)
            {
                return _radii[i];
            }
        }

        var radius = compute(unit);
        Misses++;
        _directions[_next] = unit;
        _radii[_next] = radius;
        _next = (_next + 1) % Capacity;
        _count = Math.Min(_count + 1, Capacity);
        return radius;
    }
}

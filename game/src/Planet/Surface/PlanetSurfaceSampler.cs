using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Planet.Surface;

/// <summary>
/// Radius of the generated surface of a planet along a direction (K5-22 decision 11), read from the generator graph without changing it:
/// an SDF ray from <c>R + MaxRelief + 2</c> down to <c>R − MaxRelief − 2</c> with <see cref="CoarseStrideMeters"/>, then a second ray
/// with <see cref="FineStrideMeters"/> over the coarse step that hit. Caves start ≥ 6 m under the surface, so the first hit from above is
/// the surface. Only the sign of the SDF is used, so the clipping of the graph (<see cref="PlanetGeneratorV1.SdfClipThreshold"/>) does
/// not matter. Player edits are not seen (generator only). Voxel Tools evaluates the graph with per-thread state, so one sampler may be
/// used from several threads (K5-22 step 1 checks it).
/// </summary>
public sealed class PlanetSurfaceSampler
{
    /// <summary>Stride of the first ray, in metres.</summary>
    public const float CoarseStrideMeters = 2f;

    /// <summary>Stride of the refining ray, in metres (error bound of the result).</summary>
    public const float FineStrideMeters = 0.125f;

    private const float Overshoot = 2f;

    private readonly VoxelGeneratorGraph _graph;
    private readonly float _top;
    private readonly float _bottom;

    /// <summary>Initializes a new instance of the <see cref="PlanetSurfaceSampler"/> class.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="graph">The generator graph of the planet (<see cref="GeneratorProfiles.CreateGenerator"/>).</param>
    public PlanetSurfaceSampler(PlanetParams planet, VoxelGeneratorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(graph);
        Planet = planet;
        _graph = graph;
        _top = planet.RadiusMeters + PlanetGeneratorV1.MaxReliefMeters + Overshoot;
        _bottom = planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters - Overshoot;
    }

    /// <summary>Gets the planet.</summary>
    public PlanetParams Planet { get; }

    /// <summary>Radius of the surface along a direction from the planet centre (planet frame).</summary>
    /// <param name="direction">The direction (any length &gt; 0).</param>
    /// <returns>The radius; <c>R − MaxRelief</c> when the ray finds nothing (should not happen for this generator).</returns>
    public float SurfaceRadius(NumVector3 direction)
    {
        var length = direction.Length();
        var up = length > 1e-6f && float.IsFinite(length) ? direction / length : NumVector3.UnitY;
        var godotUp = new Godot.Vector3(up.X, up.Y, up.Z);
        var coarse = _graph.RaycastSdfApprox(godotUp * _top, godotUp * _bottom, CoarseStrideMeters);
        if (coarse < 0f)
        {
            return Planet.RadiusMeters - PlanetGeneratorV1.MaxReliefMeters;
        }

        var hitRadius = _top - coarse;
        var from = MathF.Min(hitRadius + CoarseStrideMeters, _top);
        var to = MathF.Max(hitRadius - CoarseStrideMeters, _bottom);
        var fine = _graph.RaycastSdfApprox(godotUp * from, godotUp * to, FineStrideMeters);
        return fine < 0f ? hitRadius : from - fine;
    }

    /// <summary>Height of the surface over R along a direction.</summary>
    /// <param name="direction">The direction.</param>
    /// <returns>The height in metres, in [−MaxRelief; MaxRelief].</returns>
    public float SurfaceHeight(NumVector3 direction) => SurfaceRadius(direction) - Planet.RadiusMeters;
}

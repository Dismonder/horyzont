using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Testing;

/// <summary>
/// Terrain of every planet is a sphere of <see cref="SurfaceRadius"/> around the planet centre. Records edits and lets
/// tests control which areas are loaded. Raycasts do not allocate.
/// </summary>
public sealed class FakeTerrainService(float surfaceRadius = 4000f) : ITerrainService
{
    public float SurfaceRadius { get; set; } = surfaceRadius;

    /// <summary>When true every area counts as loaded; otherwise only <see cref="LoadedAreas"/>.</summary>
    public bool AllAreasLoaded { get; set; } = true;

    public List<LoadedArea> LoadedAreas { get; } = [];

    public List<LoadedArea> BlockingLoads { get; } = [];

    public List<TerrainEditOp> AppliedEdits { get; } = [];

    /// <summary>When set, <see cref="ApplyEdit"/> throws it.</summary>
    public Exception? ThrowOnApply { get; set; }

    public bool IsAreaLoaded(PlanetId planet, Vector3 center, float radius)
    {
        if (AllAreasLoaded)
        {
            return true;
        }

        foreach (var area in LoadedAreas)
        {
            if (area.Planet == planet && Vector3.Distance(area.Center, center) + radius <= area.Radius)
            {
                return true;
            }
        }

        return false;
    }

    public void LoadAreaBlocking(PlanetId planet, Vector3 center, float radius)
    {
        var area = new LoadedArea(planet, center, radius);
        BlockingLoads.Add(area);
        LoadedAreas.Add(area);
    }

    public bool Raycast(PlanetId planet, Vector3 origin, Vector3 direction, float maxDistance, out TerrainHit hit)
    {
        hit = default;
        var r = SurfaceRadius;
        var c = origin.LengthSquared() - (r * r);
        if (c <= 0)
        {
            // Inside the terrain: hit right at the origin.
            hit = new TerrainHit(origin, TerrainMath.UpAt(origin), 0f);
            return true;
        }

        var b = Vector3.Dot(origin, direction);
        var discriminant = (b * b) - c;
        if (discriminant < 0)
        {
            return false;
        }

        var t = -b - MathF.Sqrt(discriminant);
        if (t < 0 || t > maxDistance)
        {
            return false;
        }

        var position = origin + (direction * t);
        hit = new TerrainHit(position, TerrainMath.UpAt(position), t);
        return true;
    }

    public void ApplyEdit(TerrainEditOp edit)
    {
        if (ThrowOnApply is { } exception)
        {
            throw exception;
        }

        AppliedEdits.Add(edit);
    }
}

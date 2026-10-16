using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.World;
using Horyzont.Game.Planet.Surface;

using NumVector3 = System.Numerics.Vector3;

namespace Horyzont.Game.Planet.Props;

/// <summary>A prop to draw: the instance the server knows (<see cref="PropField"/> gives the very same one) and the turn around "up".</summary>
/// <param name="Instance">The prop.</param>
/// <param name="YawRadians">Rotation around <see cref="PropInstance.Up"/> in radians (the layout stores 1/65536 of a turn).</param>
public readonly record struct PropVisual(PropInstance Instance, float YawRadians);

/// <summary>
/// The anchors of the props of one region (K6-31 D20): the candidates of the frozen layout placed on the generated surface with the same
/// <see cref="PlanetSurfaceSampler.SurfaceRadius(NumVector3)"/> call as <c>VoxelTerrainService.TrySampleGeneratedSurface</c> (D6), so the
/// client draws and picks exactly what the server validates. Pure and thread safe (the sampler is), so it runs on worker threads; it
/// does what <see cref="PropField.TryGetProp"/> does for every slot of the region, without the main thread cache.
/// </summary>
public static class PropRegionAnchors
{
    private const int CellsPerRegionEdge = 8;
    private const int Slots = 4;
    private const double OffsetUnit = 1.0 / 1024.0;

    /// <summary>Gets the key of the region a prop belongs to, from the face and the region column and row.</summary>
    /// <param name="face">Face 0-5.</param>
    /// <param name="regionU">Region column.</param>
    /// <param name="regionV">Region row.</param>
    /// <returns>The key.</returns>
    public static PropRegionKey KeyOf(int face, int regionU, int regionV) =>
        PropId.Create(face, regionU * CellsPerRegionEdge, regionV * CellsPerRegionEdge, 0).Region;

    /// <summary>Direction from the planet centre to the middle of a region.</summary>
    /// <param name="region">The region.</param>
    /// <param name="cellsPerFace">Cells per face edge of the planet.</param>
    /// <returns>The unit direction.</returns>
    public static NumVector3 CentreDirection(PropRegionKey region, int cellsPerFace) =>
        CubeSphereGrid.DirectionOfPoint(
            region.Face, (region.RegionU * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0), (region.RegionV * CellsPerRegionEdge) + (CellsPerRegionEdge / 2.0),
            cellsPerFace);

    /// <summary>Finds the props of a region, ascending by <see cref="PropId"/>.</summary>
    /// <param name="planet">The planet.</param>
    /// <param name="layout">The layout of its generator profile.</param>
    /// <param name="sampler">The surface sampler of the planet (any thread).</param>
    /// <param name="region">The region.</param>
    /// <returns>The props the layout has in the region and the height band accepts.</returns>
    public static PropVisual[] Compute(PlanetParams planet, IPropLayout layout, PlanetSurfaceSampler sampler, PropRegionKey region)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(sampler);
        if (!region.IsValid || !CubeSphereGrid.TryGetCellsPerFace(planet.RadiusMeters, out var cells))
        {
            return [];
        }

        var result = new List<PropVisual>(64);
        for (var u = 0; u < CellsPerRegionEdge; u++)
        {
            for (var v = 0; v < CellsPerRegionEdge; v++)
            {
                for (var slot = 0; slot < Slots; slot++)
                {
                    var id = PropId.Create(region.Face, (region.RegionU * CellsPerRegionEdge) + u, (region.RegionV * CellsPerRegionEdge) + v, slot);
                    if (id.CellU >= cells || id.CellV >= cells || !layout.TryGetCandidate(planet.Seed, id, out var candidate))
                    {
                        continue;
                    }

                    var direction = CubeSphereGrid.DirectionOfPoint(
                        id.Face, id.CellU + (candidate.OffsetU * OffsetUnit), id.CellV + (candidate.OffsetV * OffsetUnit), cells);
                    var radius = sampler.SurfaceRadius(direction);
                    if (!float.IsFinite(radius) || !layout.Accepts(candidate.Kind, radius - planet.RadiusMeters))
                    {
                        continue;
                    }

                    var instance = new PropInstance(id, candidate.Kind, direction * radius, direction, candidate.Scale / 1000f);
                    result.Add(new PropVisual(instance, candidate.Rotation / 65536f * MathF.Tau));
                }
            }
        }

        return [.. result];
    }
}

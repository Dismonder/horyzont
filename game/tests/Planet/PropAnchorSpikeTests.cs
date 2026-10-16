using System.Diagnostics;
using System.Globalization;

using GdUnit4;

using Horyzont.Core.Ids;
using Horyzont.Core.Props;
using Horyzont.Core.Space;
using Horyzont.Game.Planet;
using Horyzont.Game.Planet.Generators;
using Horyzont.Game.Planet.Surface;

using static GdUnit4.Assertions;

namespace Horyzont.Game.Tests.Planet;

/// <summary>
/// K6-31 step D3 (spike, category Manual, no window): how long it takes to find the anchors of a prop region on a worker thread with the
/// <see cref="PlanetSurfaceSampler"/> (decision D20) and where the meshes of LOD 3 of the terrain begin (decision DO USTALENIA-6). The numbers
/// go to a file (the path in <c>HORYZONT_SPIKE_OUT</c>, default <c>prop-anchor-spike.txt</c> in the temp directory) and into the plan.
/// Nothing is gated except that the measurement ran: the numbers decide the render radius, not a pass or fail.
/// </summary>
[TestSuite]
public class PropAnchorSpikeTests
{
    private const ulong Seed = 21;
    private const int RegionEdge = 7; // 7 x 7 = 49 regions around the middle of a face
    private const int CellsPerRegion = 8;

    [TestCase(Timeout = 600000)]
    [RequireGodotRuntime]
    [Trait("Category", "Manual")]
    public async Task PropAnchors_RegionOnWorker_Timing()
    {
        var home = StarSystemGenesis.CreateWorldBodies(Seed, GeneratorProfiles.Current).Bodies[0];
        var sampler = new PlanetSurfaceSampler(home, GeneratorProfiles.CreateGenerator(home));
        AssertThat(PropLayouts.TryGet(home.GeneratorProfile, out var layout)).IsTrue();
        AssertThat(CubeSphereGrid.TryGetCellsPerFace(home.RadiusMeters, out var cells)).IsTrue();
        var centre = cells / CellsPerRegion / 2;
        var regions = new List<(int U, int V)>();
        for (var u = 0; u < RegionEdge; u++)
        {
            for (var v = 0; v < RegionEdge; v++)
            {
                regions.Add((centre + u - (RegionEdge / 2), centre + v - (RegionEdge / 2)));
            }
        }

        // One region at a time on the thread pool: the cost of a single region on a worker (what a frame budget has to plan with).
        var single = new List<double>();
        var anchors = 0;
        foreach (var (u, v) in regions)
        {
            var (milliseconds, count) = await Task.Run(() => Measure(layout!, sampler, home, cells, u, v));
            single.Add(milliseconds);
            anchors += count;
        }

        // All regions at once, as the renderer starts them after a teleport.
        var wall = Stopwatch.StartNew();
        await Task.WhenAll(regions.Select(r => Task.Run(() => Measure(layout!, sampler, home, cells, r.U, r.V))));
        wall.Stop();

        single.Sort();
        var inRadius = (int)Math.Round(Math.PI * 384.0 * 384.0 / (64.0 * 64.0));
        var report = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            K6-31 D3 spike, planet R={home.RadiusMeters} m, seed {Seed}, {regions.Count} regions, {cells} cells per face
            region on a worker (one at a time): p50 {Percentile(single, 0.5):F1} ms, p95 {Percentile(single, 0.95):F1} ms, max {single[^1]:F1} ms
            anchors per region: {anchors / (double)regions.Count:F1} (candidates the height band accepts)
            all {regions.Count} regions in parallel on the thread pool ({Environment.ProcessorCount} logical cores): {wall.Elapsed.TotalMilliseconds:F0} ms wall
            regions inside 384 m of the camera: about {inRadius}, so one cold start is about {inRadius * Percentile(single, 0.5) / 1000.0:F1} s of single-thread work
            terrain: voxel 1 m, LodDistance {PlanetTerrainFactory.LodDistance} m, mesh block {PlanetTerrainLayout.MeshBlockSize} voxels, lod count {PlanetTerrainLayout.LodCount(home)}
            """);
        var path = Environment.GetEnvironmentVariable("HORYZONT_SPIKE_OUT") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Path.GetTempPath(), "prop-anchor-spike.txt");
        await File.WriteAllTextAsync(path, report);
        Godot.GD.Print(report);
        AssertThat(single.Count).IsEqual(regions.Count);
        AssertThat(anchors > 0).IsTrue();
    }

    private static (double Milliseconds, int Count) Measure(IPropLayout layout, PlanetSurfaceSampler sampler, Horyzont.Core.World.PlanetParams planet, int cells, int regionU, int regionV)
    {
        var watch = Stopwatch.StartNew();
        var count = 0;
        for (var u = 0; u < CellsPerRegion; u++)
        {
            for (var v = 0; v < CellsPerRegion; v++)
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    var id = PropId.Create(0, (regionU * CellsPerRegion) + u, (regionV * CellsPerRegion) + v, slot);
                    if (!layout.TryGetCandidate(planet.Seed, id, out var candidate))
                    {
                        continue;
                    }

                    var direction = CubeSphereGrid.DirectionOfPoint(0, id.CellU + (candidate.OffsetU / 1024.0), id.CellV + (candidate.OffsetV / 1024.0), cells);
                    var radius = sampler.SurfaceRadius(direction);
                    if (layout.Accepts(candidate.Kind, radius - planet.RadiusMeters))
                    {
                        count++;
                    }
                }
            }
        }

        return (watch.Elapsed.TotalMilliseconds, count);
    }

    private static double Percentile(List<double> sorted, double p) => sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(p * sorted.Count))];
}

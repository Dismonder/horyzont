using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.Planet.Diagnostics;

/// <summary>
/// <c>--benchmark-report &lt;dir&gt; [--seed hex]</c>: writes <c>benchmark-report.json</c> with the block generation rate per LOD
/// (K4-19; <c>tools/bench/check_budgets.py</c> reads it). Needs no graphics. Meshing and the orbit flight are measured only by the
/// in-engine runner with a rendered scene (<c>orbitToSurface</c> stays absent here, and the budget check skips what is absent).
/// </summary>
public static class BenchmarkReport
{
    /// <summary>Format version of the report.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Runs the measurements and writes the report.</summary>
    /// <param name="directory">Existing output directory.</param>
    /// <param name="galaxySeed">Galaxy seed of the world.</param>
    /// <param name="repeats">How many times each sampler block is generated.</param>
    /// <returns>Path of the JSON file.</returns>
    public static string Run(string directory, ulong galaxySeed, int repeats = 3)
    {
        var planet = PlanetGenesis.CreatePlanet(galaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);
        var samples = GeneratorSampler.Create(planet);
        var generator = GeneratorProfiles.CreateGenerator(planet);
        var perLod = new SortedDictionary<int, List<double>>();
        using (var digest = new GeneratorDigest(generator))
        {
            // One warm-up pass so the first block of each LOD does not carry the start-up cost.
            foreach (var sample in samples)
            {
                digest.Compute(sample);
            }

            for (var round = 0; round < repeats; round++)
            {
                foreach (var sample in samples)
                {
                    var clock = Stopwatch.StartNew();
                    digest.Compute(sample);
                    clock.Stop();
                    if (!perLod.TryGetValue(sample.Lod, out var list))
                    {
                        perLod[sample.Lod] = list = [];
                    }

                    list.Add(clock.Elapsed.TotalMilliseconds);
                }
            }
        }

        var path = Path.Combine(directory, "benchmark-report.json");
        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schema", SchemaVersion);
        writer.WriteString("commit", System.Environment.GetEnvironmentVariable("HORYZONT_COMMIT") ?? "unknown");
        writer.WriteString("runtime", GeneratorReport.RuntimeName());
        writer.WriteNumber("meshBlockSize", PlanetTerrainLayout.MeshBlockSize);
        writer.WriteStartObject("generation");
        foreach (var (lod, times) in perLod)
        {
            times.Sort();
            var mean = times.Average();
            writer.WriteStartObject(lod.ToString(CultureInfo.InvariantCulture));
            writer.WriteNumber("blocks", times.Count);
            writer.WriteNumber("meanMs", Math.Round(mean, 4));
            writer.WriteNumber("p95Ms", Math.Round(times[(int)(times.Count * 0.95)], 4));
            writer.WriteNumber("blocksPerSecond", Math.Round(1000d / mean, 1));
            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.Flush();
        GD.Print($"BENCHMARK REPORT OK lods={perLod.Count} file={path}");
        return path;
    }
}

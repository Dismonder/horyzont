using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

using Godot;

using Horyzont.Core.World;
using Horyzont.Game.Planet.Generators;

namespace Horyzont.Game.Planet.Diagnostics;

/// <summary>
/// <c>--generator-report &lt;dir&gt; [--seed hex] [--dump-keys file]</c>: writes <c>generator-&lt;platform&gt;-&lt;runtime&gt;.json</c> with the digests
/// of the sampler blocks, to compare generator output between Windows and Linux and between editor and release builds
/// (<c>tools/generator/compare_reports.py</c>). Needs no graphics.
/// </summary>
public static class GeneratorReport
{
    /// <summary>Report format version understood by <c>compare_reports.py</c>.</summary>
    public const int SchemaVersion = 1;

    /// <summary>Platform name used in file names and in the report.</summary>
    public static string PlatformName() => OS.GetName().ToLowerInvariant() switch
    {
        "windows" => "windows",
        "linuxbsd" or "linux" => "linux",
        var other => other,
    };

    /// <summary>Runtime kind: editor, template_release or template_debug.</summary>
    public static string RuntimeName() =>
        OS.HasFeature("editor") ? "editor" : OS.HasFeature("release") ? "template_release" : "template_debug";

    /// <summary>
    /// Generates the report for the home planet of the galaxy seed. When <paramref name="dumpKeysFile"/> is set, the raw channel
    /// bytes of the blocks listed in it (one <c>lod x y z</c> per line) are written to <c>raw-&lt;platform&gt;-&lt;runtime&gt;.bin</c>.
    /// </summary>
    /// <param name="directory">Existing output directory.</param>
    /// <param name="galaxySeed">Galaxy seed of the world.</param>
    /// <param name="dumpKeysFile">Optional list of block keys for the raw dump.</param>
    /// <param name="maxBlocks">Limit of the sample set (the first blocks in key order), for tests; null means all.</param>
    /// <returns>Path of the JSON file.</returns>
    public static string Run(string directory, ulong galaxySeed, string? dumpKeysFile, int? maxBlocks = null)
    {
        var planet = PlanetGenesis.CreatePlanet(galaxySeed, PlanetGenesis.HomePlanetIndex, GeneratorProfiles.Current);
        var samples = GeneratorSampler.Create(planet);
        if (maxBlocks is { } limit && limit < samples.Count)
        {
            samples = [.. samples.Take(limit)];
        }

        var platform = PlatformName();
        var runtime = RuntimeName();

        var generator = GeneratorProfiles.CreateGenerator(planet);
        var digests = new List<BlockDigest>(samples.Count);
        var microseconds = new List<double>(samples.Count);
        using (var digest = new GeneratorDigest(generator))
        {
            foreach (var sample in samples)
            {
                var clock = Stopwatch.StartNew();
                var result = digest.Compute(sample);
                clock.Stop();
                digests.Add(result);
                microseconds.Add(clock.Elapsed.TotalMilliseconds * 1000d);
            }
        }

        microseconds.Sort();
        var median = microseconds.Count == 0 ? 0d : microseconds[microseconds.Count / 2];

        var path = Path.Combine(directory, $"generator-{platform}-{runtime}.json");
        WriteJson(path, planet, galaxySeed, platform, runtime, digests, median);

        if (dumpKeysFile is not null)
        {
            WriteRaw(Path.Combine(directory, $"raw-{platform}-{runtime}.bin"), generator, ReadKeys(dumpKeysFile));
        }

        GD.Print($"GENERATOR REPORT OK blocks={digests.Count} file={path}");
        return path;
    }

    private static void WriteJson(
        string path, PlanetParams planet, ulong galaxySeed, string platform, string runtime, List<BlockDigest> digests, double medianMicroseconds)
    {
        var engine = (string)Engine.GetVersionInfo()["string"];
        var voxel = $"{VoxelEngine.GetVersionMajor()}.{VoxelEngine.GetVersionMinor()}.{VoxelEngine.GetVersionPatch()} {VoxelEngine.GetVersionGitHash()}";
        var commit = System.Environment.GetEnvironmentVariable("HORYZONT_COMMIT") ?? "unknown";

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        writer.WriteNumber("schema", SchemaVersion);
        writer.WriteString("profile", planet.GeneratorProfile);
        writer.WriteString("seed", Hex(galaxySeed));
        writer.WriteString("planet_seed", Hex(planet.Seed));
        writer.WriteNumber("radius", planet.RadiusMeters);
        writer.WriteString("platform", platform);
        writer.WriteString("runtime", runtime);
        writer.WriteString("engine", engine);
        writer.WriteString("voxel", voxel);
        writer.WriteString("commit", commit);
        writer.WriteString("median_us", medianMicroseconds.ToString("F1", CultureInfo.InvariantCulture));
        writer.WriteStartArray("blocks");
        foreach (var d in digests)
        {
            writer.WriteStartObject();
            writer.WriteNumber("lod", d.Block.Lod);
            writer.WriteNumber("x", d.Block.X);
            writer.WriteNumber("y", d.Block.Y);
            writer.WriteNumber("z", d.Block.Z);
            writer.WriteString("sdf", Hex(d.Sdf));
            writer.WriteString("mat", Hex(d.Material));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static List<BlockSample> ReadKeys(string file)
    {
        var keys = new List<BlockSample>();
        foreach (var line in File.ReadLines(file, Encoding.UTF8))
        {
            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#'))
            {
                continue;
            }

            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lod)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                || !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var z)
                || lod is < 0 or > 15
                || !OriginInRange(lod, x)
                || !OriginInRange(lod, y)
                || !OriginInRange(lod, z)
                || keys.Count >= 4096)
            {
                throw new ArgumentException($"--dump-keys {file}: bad line '{text}' (expected 'lod x y z', at most 4096 lines).");
            }

            keys.Add(new BlockSample(lod, x, y, z));
        }

        return keys;
    }

    /// <summary>Block origins beyond 2^24 voxels would overflow <see cref="BlockSample.Origin"/> and lie far outside any planet.</summary>
    private static bool OriginInRange(int lod, int key)
    {
        const long MaxOriginVoxels = 1L << 24;
        return Math.Abs((long)key * ((long)BlockSample.BlockSize << lod)) <= MaxOriginVoxels;
    }

    /// <summary>Raw dump: for each block int32 LE lod, x, y, z, then the SDF channel bytes and the material channel bytes.</summary>
    private static void WriteRaw(string path, VoxelGenerator generator, List<BlockSample> keys)
    {
        using var buffer = new VoxelBuffer();
        using var format = TerrainChannels.CreateFormat();
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        foreach (var key in keys)
        {
            buffer.Create(BlockSample.BlockSize, BlockSample.BlockSize, BlockSample.BlockSize);
            format.ConfigureBuffer(buffer);
            var (x, y, z) = key.Origin;
            generator.GenerateBlock(buffer, new Vector3(x, y, z), key.Lod);
            writer.Write(key.Lod);
            writer.Write(key.X);
            writer.Write(key.Y);
            writer.Write(key.Z);
            writer.Write(buffer.GetChannelAsByteArray(TerrainChannels.Sdf));
            writer.Write(buffer.GetChannelAsByteArray(TerrainChannels.Material));
        }
    }

    private static string Hex(ulong value) => value.ToString("x16", CultureInfo.InvariantCulture);
}

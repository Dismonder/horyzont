using System.Numerics;

using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Tests;

/// <summary>
/// Records the golden terrain journal corpus of <see cref="TerrainJournalGoldenCorpusTests"/>. Run only on purpose, with
/// HORYZONT_GOLDEN_TERRAIN_OUT set to the corpus source directory; a recorded corpus is frozen (K2-09, decision D1) and a new one
/// belongs to a new operation format version.
/// </summary>
public sealed class TerrainJournalGoldenCorpusRecorder
{
    private const string OutputVariable = "HORYZONT_GOLDEN_TERRAIN_OUT";

    private static readonly PlanetId _home = new(1);

    [Fact]
    [Trait("Category", "Manual")]
    public void Record_WritesCorpus()
    {
        var output = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrEmpty(output))
        {
            return;
        }

        Directory.CreateDirectory(output);
        foreach (var (name, entries) in Journals())
        {
            File.WriteAllBytes(Path.Combine(output, name + ".journal"), TerrainJournalGoldenCorpusTests.WriteJournal(entries));
            File.WriteAllText(Path.Combine(output, name + ".expected.txt"), TerrainJournalGoldenCorpusTests.Report(entries));
        }
    }

    private static IEnumerable<(string Name, List<JournalEntry> Entries)> Journals()
    {
        yield return ("add_remove", Sequence(
            Remove(new Vector3(0.5f, 3999.5f, 0.5f), 0.5f),
            Add(new Vector3(0.5f, 3999.5f, 0.5f), 1f, 1),
            Remove(new Vector3(2f, 3998f, -1f), 3f),
            Add(new Vector3(2f, 3998f, -1f), 2.5f, 2),
            Remove(new Vector3(2f, 3998f, -1f), 0.75f),
            Add(new Vector3(1f, 4001f, 1f), 1.5f, 1)));

        yield return ("block_boundaries", Sequence(
            Remove(new Vector3(16f, 4000f, 0f), 1f),
            Remove(new Vector3(0f, 4000f, 0f), 0.5f),
            Add(new Vector3(15.999f, 3999.999f, 31.999f), 0.5f, 1),
            Remove(new Vector3(48f, 4016f, -16f), 3f),
            Add(new Vector3(32f, 4000f, 32f), 12f, 2),
            Remove(new Vector3(12f, 3996f, 12f), 16f),
            Add(new Vector3(4f, 4004f, 4f), 0.001f, 1)));

        yield return ("negative_coordinates", Sequence(
            Remove(new Vector3(-0.5f, 3999.5f, -0.5f), 0.5f),
            Remove(new Vector3(-16f, 3990f, -16.001f), 2f),
            Remove(new Vector3(-1f, -4000f, -1f), 1f),
            Add(new Vector3(-4000f, -0.25f, 7f), 3f, 2),
            Add(new Vector3(0f, 0f, -4000f), 2f, 1),
            Remove(new Vector3(-0.001f, -0.001f, -0.001f), 4.5f)));

        var rejected = new List<JournalEntry>
        {
            Entry(1, 1, Remove(new Vector3(0f, 3999f, 0f), 1f)),
            Entry(2, 1, new TerrainEditOp(new PlanetId(99), TerrainShape.Sphere, new Vector3(0f, 3999f, 0f), 1f, TerrainEditMode.Remove, 0)),
            Entry(2, 1, Remove(new Vector3(0f, 3999f, 0f), 0f)),
            Entry(2, 1, Remove(new Vector3(0f, 3999f, 0f), -1f)),
            Entry(2, 1, Remove(new Vector3(0f, 3999f, 0f), MathF.BitIncrement(16f))),
            Entry(2, 2, Remove(new Vector3(0f, 3999f, 0f), 16f)),
            Entry(3, 2, Remove(new Vector3(0f, 5016.01f, 0f), 1f)),
            Entry(3, 2, Remove(new Vector3(0f, 5016f, 0f), 1f)),
            Entry(4, 2, Add(new Vector3(0f, 3999f, 0f), 1f, 0)),
            Entry(4, 2, Add(new Vector3(0f, 3999f, 0f), 1f, 3)),
            Entry(4, 2, Remove(new Vector3(0f, 3999f, 0f), 1f, 1)),
            Entry(4, 3, Add(new Vector3(0f, 3999f, 0f), 1f, 2)),
            Entry(5, 2, Remove(new Vector3(0f, 3999f, 0f), 1f)),
            Entry(6, 3, Remove(new Vector3(0f, 3999f, 0f), 1f)),
            Entry(5, 4, Remove(new Vector3(3f, 3999f, 0f), 2f)),
        };
        yield return ("rejected_entries", rejected);

        var random = new Random(20261009);
        var mixed = new List<TerrainEditOp>();
        for (var i = 0; i < 200; i++)
        {
            var center = new Vector3(
                (float)((random.NextDouble() * 64) - 32),
                3990f + (float)(random.NextDouble() * 20),
                (float)((random.NextDouble() * 64) - 32));
            var radius = 0.5f + (float)(random.NextDouble() * 2.5);
            mixed.Add(random.Next(2) == 0 ? Remove(center, radius) : Add(center, radius, (byte)(1 + random.Next(2))));
        }

        yield return ("mixed_long", Sequence([.. mixed]));
    }

    private static List<JournalEntry> Sequence(params TerrainEditOp[] operations)
    {
        var entries = new List<JournalEntry>(operations.Length);
        for (var i = 0; i < operations.Length; i++)
        {
            entries.Add(Entry((ulong)i + 1, (ulong)(i / 3) + 1, operations[i]));
        }

        return entries;
    }

    private static JournalEntry Entry(ulong id, ulong tick, TerrainEditOp operation) =>
        new(new OpId(id), new Tick(tick), id % 2 == 0 ? new PlayerId(1) : null, operation);

    private static TerrainEditOp Remove(Vector3 center, float radius, byte material = 0) =>
        new(_home, TerrainShape.Sphere, center, radius, TerrainEditMode.Remove, material);

    private static TerrainEditOp Add(Vector3 center, float radius, byte material) =>
        new(_home, TerrainShape.Sphere, center, radius, TerrainEditMode.Add, material);
}

using System.Text;

using Horyzont.Core.Intents;
using Horyzont.Core.World;

namespace Horyzont.Core.Tests.Survival;

/// <summary>
/// K6-35 B5: the texts of the death and the respawn exist in Polish and in English. The files are read as text (the import of the translations needs the editor), so the test also
/// checks that the new file is registered in the project and has the import file the editor would write.
/// </summary>
public sealed class DeathContentTests
{
    private static readonly string[] _fixedKeys =
    [
        "DEATH_TITLE", "DEATH_RESPAWNING", "DEATH_RESPAWN_WAIT", "DEATH_RESPAWN_READY", "DEATH_AT_BED", "DEATH_AT_START", "DEATH_ITEMS_KEPT", "DEATH_ITEMS_TOMBSTONE",
        "DEATH_ITEMS_TOMBSTONES", "DEATH_FOOD_LOST", "DEATH_CONTINUE", "DEATH_SETTING_PENALTY", "TOMBSTONE_NAME", "TOMBSTONE_PROMPT_OPEN", "TOMBSTONE_TAKE_ALL",
        "TOMBSTONE_MARKER", "TOMBSTONE_OTHER_PLANET", "TOMBSTONE_DISTANCE", "TOMBSTONE_REMAINDER", "TOMBSTONE_PRIVATE",
    ];

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Horyzont.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Horyzont.sln was not found above the test binaries.");
    }

    private static string LocalePath(string file) => Path.Combine(RepoRoot(), "game", "locale", file);

    /// <summary>Reads a translation file: the rows after the header as key, English, Polish. A field in quotes may hold commas and doubled quotes.</summary>
    private static List<(string Key, string En, string Pl)> Read(string file)
    {
        var rows = new List<(string, string, string)>();
        var lines = File.ReadAllLines(LocalePath(file), Encoding.UTF8);
        Assert.Equal("keys,en,pl", lines[0]);
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var fields = Split(line);
            Assert.True(fields.Count == 3, $"{file}: {line} has {fields.Count} fields, not 3");
            rows.Add((fields[0], fields[1], fields[2]));
        }

        return rows;
    }

    private static List<string> Split(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    [Fact]
    public void Translations_HavePolishAndEnglishKeys()
    {
        var death = Read("death.csv");
        var tools = Read("tools.csv");
        Assert.NotEmpty(death);

        // The keys the code asks for: the fixed ones, a name and a description for every penalty, and the text of the new refusal (the key is REJECT_ and the upper snake case name).
        var required = _fixedKeys
            .Concat(Enum.GetValues<DeathPenalty>().SelectMany(p => new[] { "DEATH_PENALTY_" + p.ToString().ToUpperInvariant(), "DEATH_PENALTY_" + p.ToString().ToUpperInvariant() + "_DESC" }))
            .ToList();
        Assert.Equal(3, Enum.GetValues<DeathPenalty>().Length);
        Assert.Equal(_fixedKeys.Length + 6, required.Count);

        var byKey = death.ToDictionary(r => r.Key);
        Assert.Equal(death.Count, byKey.Count);
        foreach (var key in required)
        {
            Assert.True(byKey.TryGetValue(key, out var row), $"death.csv has no {key}");
            Assert.False(string.IsNullOrWhiteSpace(row.En), $"{key} has no English text");
            Assert.False(string.IsNullOrWhiteSpace(row.Pl), $"{key} has no Polish text");
            Assert.NotEqual(key, row.En);
            Assert.NotEqual(key, row.Pl);
        }

        // Nothing unused hides in the file, and the number placeholder is in both languages or in neither.
        Assert.Equal(required.Order(), byKey.Keys.Order());
        Assert.All(death, row => Assert.Equal(row.En.Contains("{0}", StringComparison.Ordinal), row.Pl.Contains("{0}", StringComparison.Ordinal)));
        Assert.Contains("{0}", byKey["DEATH_ITEMS_TOMBSTONES"].En, StringComparison.Ordinal);
        Assert.Contains("{0}", byKey["TOMBSTONE_DISTANCE"].Pl, StringComparison.Ordinal);

        var refusal = tools.Single(r => r.Key == "REJECT_" + "IN_TRANSITION");
        Assert.Equal("InTransition", RejectReason.InTransition.ToString());
        Assert.False(string.IsNullOrWhiteSpace(refusal.En));
        Assert.False(string.IsNullOrWhiteSpace(refusal.Pl));
        Assert.NotEqual(refusal.En, refusal.Pl);
    }

    [Fact]
    public void DeathFile_IsRegisteredLikeTheOthers()
    {
        var import = File.ReadAllText(LocalePath("death.csv.import"));
        Assert.Contains("source_file=\"res://locale/death.csv\"", import, StringComparison.Ordinal);
        Assert.Contains("importer=\"csv_translation\"", import, StringComparison.Ordinal);

        var project = File.ReadAllText(Path.Combine(RepoRoot(), "game", "project.godot"));
        Assert.Contains("res://locale/death.en.translation", project, StringComparison.Ordinal);
        Assert.Contains("res://locale/death.pl.translation", project, StringComparison.Ordinal);
    }
}

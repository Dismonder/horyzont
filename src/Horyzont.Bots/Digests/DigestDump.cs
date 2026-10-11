using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Horyzont.Bots.Digests;

/// <summary>
/// The dump of a check break: one JSON object per line, <c>{"kind":…,"key":…,"version":…,"hash":"16 hex digits"}</c> (constructs add
/// <c>"pos":[x,y,z]</c>, their origin), sorted by kind and natural key order, so equal states give byte-identical files. A bot dump
/// starts with one <c>{"kind":"bot",…}</c> line (name, role, feet) that <see cref="Read"/> skips. <c>tools/coop/compare_dumps.py</c>
/// reads the format.
/// </summary>
public static class DigestDump
{
    /// <summary>Writes the entries, sorted.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="entries">Entries in any order.</param>
    public static void Write(TextWriter writer, IEnumerable<DigestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.ToList();
        sorted.Sort(DigestEntryComparer.Instance);
        foreach (var entry in sorted)
        {
            writer.Write(Line(entry));
            writer.Write('\n');
        }
    }

    /// <summary>Reads a dump written by <see cref="Write"/>.</summary>
    /// <param name="reader">Source.</param>
    /// <returns>The entries in file order.</returns>
    /// <exception cref="InvalidDataException">A line is not an entry.</exception>
    public static IReadOnlyList<DigestEntry> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var entries = new List<DigestEntry>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var text = root.GetProperty("kind").GetString();
                if (text == "bot")
                {
                    continue;
                }

                var kind = text switch
                {
                    "construct" => DigestKind.Construct,
                    "terrain" => DigestKind.Terrain,
                    var other => throw new FormatException("Unknown entry kind " + other),
                };
                var key = root.GetProperty("key").GetString() ?? throw new FormatException("Entry without key.");
                var version = root.GetProperty("version").GetUInt32();
                var hash = ulong.Parse(root.GetProperty("hash").GetString() ?? string.Empty, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                Vector3? position = null;
                if (root.TryGetProperty("pos", out var pos))
                {
                    position = new Vector3(pos[0].GetSingle(), pos[1].GetSingle(), pos[2].GetSingle());
                }

                entries.Add(new DigestEntry(kind, key, version, hash, ParseSortKey(kind, key), position));
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
            {
                throw new InvalidDataException("Bad dump line: " + line, exception);
            }
        }

        return entries;
    }

    /// <summary>Digest of a set of entries; the input order does not matter.</summary>
    /// <param name="entries">Entries.</param>
    /// <returns>XxHash64 over the sorted entries.</returns>
    public static ulong Combine(IEnumerable<DigestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sorted = entries.ToList();
        sorted.Sort(DigestEntryComparer.Instance);
        var hash = new XxHash64();
        Span<byte> number = stackalloc byte[1 + 4 + 8];
        foreach (var entry in sorted)
        {
            number[0] = (byte)entry.Kind;
            BinaryPrimitives.WriteUInt32LittleEndian(number[1..], entry.Version);
            BinaryPrimitives.WriteUInt64LittleEndian(number[5..], entry.Hash);
            hash.Append(number);
            hash.Append(Encoding.UTF8.GetBytes(entry.Key));
        }

        return hash.GetCurrentHashAsUInt64();
    }

    private static string Line(DigestEntry entry)
    {
        var kind = entry.Kind == DigestKind.Construct ? "construct" : "terrain";
        var position = entry.Position is { } p ? ",\"pos\":" + Vector(p) : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"kind\":\"{kind}\",\"key\":\"{entry.Key}\",\"version\":{entry.Version},\"hash\":\"{entry.Hash:x16}\"{position}}}");
    }

    /// <summary>Formats a vector as a JSON array that reads back to the same floats.</summary>
    /// <param name="value">The vector.</param>
    /// <returns>The text.</returns>
    public static string Vector(Vector3 value) =>
        string.Create(CultureInfo.InvariantCulture, $"[{value.X:R},{value.Y:R},{value.Z:R}]");

    /// <summary>Writes the header line of a bot dump.</summary>
    /// <param name="writer">Destination.</param>
    /// <param name="name">Bot name.</param>
    /// <param name="role">Role (lower case).</param>
    /// <param name="inGame">Whether the bot is in the game.</param>
    /// <param name="feet">Feet of its latest own state.</param>
    public static void WriteBotHeader(TextWriter writer, string name, string role, bool inGame, Vector3 feet)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"kind\":\"bot\",\"key\":\"{name}\",\"role\":\"{role}\",\"in_game\":{(inGame ? "true" : "false")},\"feet\":{Vector(feet)}}}"));
        writer.Write('\n');
    }

    private static (long, long, long, long, long) ParseSortKey(DigestKind kind, string key)
    {
        var parts = key.Split('/');
        if ((kind == DigestKind.Construct && parts.Length != 1) || (kind == DigestKind.Terrain && parts.Length != 5))
        {
            throw new FormatException("Key " + key + " does not fit its kind.");
        }

        var values = new long[5];
        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = long.Parse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        return (values[0], values[1], values[2], values[3], values[4]);
    }
}

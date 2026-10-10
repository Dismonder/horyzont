using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;
using System.Text;

using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Serialization;
using Horyzont.Core.Simulation;
using Horyzont.Testing;

using Microsoft.Extensions.Logging.Abstractions;

namespace Horyzont.Core.Tests;

/// <summary>
/// Frozen golden corpus of terrain journals (K2-09, gate decision D1). The journals and their expected reports were recorded
/// on main before K2-09 changed Core. Neither the corpus files nor this file may change afterwards: a change in the report
/// means the semantics of applying operations changed, which needs a new operation format version (docs/ZASADY.md, point 3).
/// </summary>
public sealed class TerrainJournalGoldenCorpusTests
{
    /// <summary>Journal file format version of the corpus container (not the operation format).</summary>
    internal const byte JournalFileVersion = 1;

    private const int MaxOperationFrameBytes = 1024;

    internal static readonly string[] JournalNames =
    [
        "add_remove",
        "block_boundaries",
        "negative_coordinates",
        "rejected_entries",
        "mixed_long",
    ];

    public static TheoryData<string> Journals => new(JournalNames);

    internal static string CorpusDirectory => Path.Combine(AppContext.BaseDirectory, "Golden", "TerrainJournals");

    [Theory]
    [MemberData(nameof(Journals))]
    public void Replay_GoldenJournal_MatchesRecordedReport(string name)
    {
        var entries = ReadJournal(File.ReadAllBytes(Path.Combine(CorpusDirectory, name + ".journal")));
        var expected = File.ReadAllText(Path.Combine(CorpusDirectory, name + ".expected.txt")).ReplaceLineEndings("\n");

        Assert.Equal(expected, Report(entries));
    }

    [Fact]
    public void Corpus_EveryJournalHasATheoryCase()
    {
        var files = Directory.GetFiles(CorpusDirectory, "*.journal").Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal);
        var cases = JournalNames.Order(StringComparer.Ordinal);

        Assert.Equal(cases, files);
    }

    /// <summary>
    /// Replays the entries on a fresh default world with a fake terrain. Entries that Replay rejects are reported and skipped,
    /// so a corpus can probe preconditions between accepted entries. The report holds the state hash after every entry,
    /// the final block versions and a hash of the fake terrain data.
    /// </summary>
    internal static string Report(IReadOnlyList<JournalEntry> entries)
    {
        var state = WorldFactory.CreateDefault();
        var terrain = new FakeTerrainService();
        var simulation = new ServerSimulation(state, terrain, new InMemoryJournal(), new GameRules(), NullLogger<ServerSimulation>.Instance);
        var report = new StringBuilder();
        foreach (var entry in entries)
        {
            try
            {
                simulation.Replay(entry);
                report.Append(CultureInfo.InvariantCulture, $"applied {entry.Id.Value} state={StateHasher.Hash(state):x16}\n");
            }
            catch (ReplayFailedException exception)
            {
                report.Append(CultureInfo.InvariantCulture, $"rejected {entry.Id.Value} {exception.Message}\n");
            }
        }

        report.Append(CultureInfo.InvariantCulture, $"last_op={state.LastOpId.Value} tick={state.CurrentTick.Value}\n");
        report.Append(CultureInfo.InvariantCulture, $"state={StateHasher.Hash(state):x16}\n");
        report.Append(CultureInfo.InvariantCulture, $"edits={terrain.AppliedEdits.Count} blocks={terrain.Blocks.Count}\n");
        report.Append(CultureInfo.InvariantCulture, $"terrain_data={TerrainDataHash(terrain):x16}\n");
        foreach (var version in state.TerrainVersions.All)
        {
            var key = version.Key;
            report.Append(CultureInfo.InvariantCulture, $"version {key.Planet.Value} {key.Lod} {key.X} {key.Y} {key.Z} = {version.Version}\n");
        }

        return report.ToString();
    }

    /// <summary>Corpus container: version byte, entry count, then per entry id, tick, optional actor and the operation frame.</summary>
    internal static byte[] WriteJournal(IReadOnlyList<JournalEntry> entries)
    {
        var w = new ByteWriter();
        var frame = new ByteWriter();
        w.WriteByte(JournalFileVersion);
        w.WriteVarUInt((ulong)entries.Count);
        foreach (var entry in entries)
        {
            w.WriteVarUInt(entry.Id.Value);
            w.WriteVarUInt(entry.Tick.Value);
            w.WriteBool(entry.Actor is not null);
            if (entry.Actor is { } actor)
            {
                w.WriteVarUInt(actor.Value);
            }

            frame.Reset();
            OperationCodec.Write(frame, entry.Operation);
            w.WriteBytes(frame.WrittenSpan, MaxOperationFrameBytes);
        }

        return w.WrittenSpan.ToArray();
    }

    internal static List<JournalEntry> ReadJournal(ReadOnlySpan<byte> bytes)
    {
        var r = new ByteReader(bytes);
        Assert.Equal(JournalFileVersion, r.ReadByte());
        var count = r.ReadCount(100_000);
        var entries = new List<JournalEntry>(count);
        for (var i = 0; i < count; i++)
        {
            var id = new OpId(r.ReadVarUInt());
            var tick = new Tick(r.ReadVarUInt());
            PlayerId? actor = r.ReadBool() ? new PlayerId(r.ReadVarUInt()) : null;
            var frameBytes = r.ReadBytes(MaxOperationFrameBytes);
            var frame = new ByteReader(frameBytes);
            var operation = OperationCodec.Read(ref frame);
            frame.EnsureEnd();
            entries.Add(new JournalEntry(id, tick, actor, operation));
        }

        r.EnsureEnd();
        return entries;
    }

    private static ulong TerrainDataHash(FakeTerrainService terrain)
    {
        var hash = new XxHash64();
        Span<byte> key = stackalloc byte[21];
        Span<byte> length = stackalloc byte[4];
        foreach (var pair in terrain.Blocks.OrderBy(p => p.Key))
        {
            BinaryPrimitives.WriteUInt64LittleEndian(key, pair.Key.Planet.Value);
            key[8] = pair.Key.Lod;
            BinaryPrimitives.WriteInt32LittleEndian(key[9..], pair.Key.X);
            BinaryPrimitives.WriteInt32LittleEndian(key[13..], pair.Key.Y);
            BinaryPrimitives.WriteInt32LittleEndian(key[17..], pair.Key.Z);
            hash.Append(key);
            BinaryPrimitives.WriteInt32LittleEndian(length, pair.Value.Length);
            hash.Append(length);
            hash.Append(pair.Value);
        }

        return hash.GetCurrentHashAsUInt64();
    }
}

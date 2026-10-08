using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;

namespace Horyzont.Testing;

/// <summary>Journal kept in a list.</summary>
public sealed class InMemoryJournal : IJournalSink
{
    public List<JournalEntry> Entries { get; } = [];

    public void Append(JournalEntry entry) => Entries.Add(entry);
}

using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;

namespace Horyzont.Persistence.Writing;

/// <summary>Journal of the simulation backed by a <see cref="PersistenceWriter"/>; never blocks.</summary>
/// <param name="writer">The writer.</param>
public sealed class JournalSink(PersistenceWriter writer) : IJournalSink
{
    /// <inheritdoc />
    public void Append(JournalEntry entry) => writer.EnqueueJournal(entry);
}

using Horyzont.Core.Operations;

namespace Horyzont.Core.Simulation;

/// <summary>Receives applied operations; must not block (the writer persists in the background, K1-05).</summary>
public interface IJournalSink
{
    /// <summary>Appends an entry.</summary>
    /// <param name="entry">The entry.</param>
    void Append(JournalEntry entry);
}

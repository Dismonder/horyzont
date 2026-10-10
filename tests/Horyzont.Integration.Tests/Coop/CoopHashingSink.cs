using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;

namespace Horyzont.Integration.Tests.Coop;

/// <summary>
/// Forwards entries to the real sink and remembers the state hash right after each operation (the simulation journals after applying),
/// so a world recovered from disk can be compared with the live one at exactly its last durable operation. The state is looked up late
/// because the runtime creates it while it starts.
/// </summary>
internal sealed class CoopHashingSink(Func<WorldState> state, IJournalSink inner) : IJournalSink
{
    public Dictionary<OpId, ulong> HashAt { get; } = [];

    public void Append(JournalEntry entry)
    {
        HashAt[entry.Id] = StateHasher.Hash(state());
        inner.Append(entry);
    }
}

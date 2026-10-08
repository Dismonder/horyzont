using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;

namespace Horyzont.Integration.Tests.Support;

/// <summary>
/// Forwards entries to the real sink and records the state hash right after each operation (the simulation journals
/// after applying), so a recovered world can be compared with the live one at exactly its last operation.
/// </summary>
internal sealed class HashingJournalSink(WorldState state, IJournalSink inner) : IJournalSink
{
    public Dictionary<OpId, ulong> HashAt { get; } = [];

    public void Append(JournalEntry entry)
    {
        HashAt[entry.Id] = StateHasher.Hash(state);
        inner.Append(entry);
    }
}

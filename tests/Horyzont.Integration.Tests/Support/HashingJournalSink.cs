using Horyzont.Core.Hashing;
using Horyzont.Core.Ids;
using Horyzont.Core.Operations;
using Horyzont.Core.Simulation;
using Horyzont.Core.World;
using Horyzont.Core.World.Terrain;

namespace Horyzont.Integration.Tests.Support;

/// <summary>
/// Forwards entries to the real sink and records the state hash and the terrain block bytes right after each operation
/// (the simulation journals after applying), so a recovered world can be compared with the live one at exactly its last operation.
/// </summary>
internal sealed class HashingJournalSink(
    WorldState state, Func<IReadOnlyDictionary<TerrainBlockKey, byte[]>> terrain, IJournalSink inner) : IJournalSink
{
    public Dictionary<OpId, ulong> HashAt { get; } = [];

    /// <summary>Block bytes in memory after each operation (arrays are replaced, never mutated, so a shallow copy is a snapshot).</summary>
    public Dictionary<OpId, Dictionary<TerrainBlockKey, byte[]>> TerrainAt { get; } = [];

    public void Append(JournalEntry entry)
    {
        HashAt[entry.Id] = StateHasher.Hash(state);
        TerrainAt[entry.Id] = new Dictionary<TerrainBlockKey, byte[]>(terrain());
        inner.Append(entry);
    }
}

using Horyzont.Core.Ids;

namespace Horyzont.Core.Operations;

/// <summary>An operation in the journal.</summary>
/// <param name="Id">Consecutive operation number.</param>
/// <param name="Tick">Tick of the operation.</param>
/// <param name="Actor">Player whose intent produced it; null for server operations.</param>
/// <param name="Operation">The operation.</param>
public sealed record JournalEntry(OpId Id, Tick Tick, PlayerId? Actor, Operation Operation);

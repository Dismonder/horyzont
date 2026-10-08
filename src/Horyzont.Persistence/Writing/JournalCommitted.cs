using Horyzont.Core.Ids;

namespace Horyzont.Persistence.Writing;

/// <summary>The journal is durable up to <paramref name="UpTo"/>.</summary>
/// <param name="UpTo">Last durable operation.</param>
public sealed record JournalCommitted(OpId UpTo) : PersistenceEvent;

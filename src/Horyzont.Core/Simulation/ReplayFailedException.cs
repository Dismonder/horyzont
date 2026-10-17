using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Simulation;

/// <summary>A journal entry could not be replayed. Replay stops; operations are never skipped.</summary>
public sealed class ReplayFailedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ReplayFailedException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Message.</param>
    public ReplayFailedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">Message.</param>
    /// <param name="innerException">Cause.</param>
    public ReplayFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for an entry.</summary>
    /// <param name="opId">Operation number.</param>
    /// <param name="kind">Operation kind.</param>
    /// <param name="reason">Why it failed.</param>
    public ReplayFailedException(OpId opId, OperationKind kind, string reason)
        : base($"Replay of operation {opId.Value} ({kind}) failed: {reason}")
    {
        OpId = opId;
        Kind = kind;
    }

    /// <summary>Creates the exception for an entry whose precondition does not hold.</summary>
    /// <param name="opId">Operation number.</param>
    /// <param name="kind">Operation kind.</param>
    /// <param name="componentIndex">Index of the failing component of a compound operation, null for other operations.</param>
    /// <param name="reason">Why it failed.</param>
    public ReplayFailedException(OpId opId, OperationKind kind, int? componentIndex, string reason)
        : base(componentIndex is { } index
            ? $"Replay of operation {opId.Value} ({kind}) failed in component {index}: {reason}"
            : $"Replay of operation {opId.Value} ({kind}) failed: {reason}")
    {
        OpId = opId;
        Kind = kind;
        ComponentIndex = componentIndex;
    }

    /// <summary>Operation that failed.</summary>
    public OpId OpId { get; }

    /// <summary>Kind of the operation that failed.</summary>
    public OperationKind? Kind { get; }

    /// <summary>Index of the failing component of a compound operation; null for other operations and other failures.</summary>
    public int? ComponentIndex { get; }
}

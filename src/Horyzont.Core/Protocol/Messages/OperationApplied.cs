using Horyzont.Core.Ids;
using Horyzont.Core.Operations;

namespace Horyzont.Core.Protocol.Messages;

/// <summary>An operation the server applied.</summary>
/// <param name="Id">Operation number.</param>
/// <param name="Tick">Tick.</param>
/// <param name="Operation">The operation.</param>
/// <param name="ConstructChanges">Construct versions after the operation.</param>
public sealed record OperationApplied(OpId Id, Tick Tick, Operation Operation, IReadOnlyList<ConstructChange> ConstructChanges) : Message
{
    /// <inheritdoc />
    public override MessageType Type => MessageType.OperationApplied;
}

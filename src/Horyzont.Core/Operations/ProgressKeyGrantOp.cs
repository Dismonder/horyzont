namespace Horyzont.Core.Operations;

/// <summary>The world gets a progress key (K6-32, operation 45); reversible, may be a component of a <see cref="CompoundOp"/>.</summary>
/// <param name="Key">The key, 1 to 64 bytes of UTF-8.</param>
public sealed record ProgressKeyGrantOp(string Key) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.ProgressKeyGrant;
}

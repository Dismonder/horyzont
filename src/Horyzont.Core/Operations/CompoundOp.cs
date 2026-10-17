namespace Horyzont.Core.Operations;

/// <summary>
/// The components of one action as one journal entry: all of them apply or none (spec 03). Format 1: 1..4096 components and at most
/// 160 KiB encoded, no nesting; components 31-35 and at most one <see cref="TerrainEditOp"/>, which must be the last one.
/// </summary>
/// <param name="Components">Components in the order they are applied.</param>
public sealed record CompoundOp(IReadOnlyList<Operation> Components) : Operation
{
    /// <inheritdoc />
    public override OperationKind Kind => OperationKind.Compound;
}

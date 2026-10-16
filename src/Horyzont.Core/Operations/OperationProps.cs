namespace Horyzont.Core.Operations;

/// <summary>
/// Finds the prop changes of an operation: a top-level <see cref="PropHarvestOp"/> or <see cref="PropRegrowOp"/> or such components of a
/// <see cref="CompoundOp"/>. Every place that recognises prop changes uses this (K6-31 D18).
/// </summary>
public static class OperationProps
{
    /// <summary>Appends the prop changes of an operation in component order.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="changes">Receives the changes; not cleared.</param>
    public static void CollectChanges(Operation operation, List<PropChange> changes)
    {
        // K6-31 A3.
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(changes);
    }
}

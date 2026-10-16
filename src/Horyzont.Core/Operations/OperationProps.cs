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
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(changes);
        switch (operation)
        {
            case CompoundOp compound:
                var components = compound.Components;
                for (var i = 0; i < components.Count; i++)
                {
                    AddChange(components[i], changes);
                }

                break;
            default:
                AddChange(operation, changes);
                break;
        }
    }

    private static void AddChange(Operation operation, List<PropChange> changes)
    {
        switch (operation)
        {
            case PropHarvestOp harvest:
                changes.Add(new PropChange(harvest.Planet, harvest.Prop, true));
                break;
            case PropRegrowOp regrow:
                changes.Add(new PropChange(regrow.Planet, regrow.Prop, false));
                break;
        }
    }
}

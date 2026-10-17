using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Operations;

/// <summary>
/// Finds the reparent of an operation: a top-level <see cref="EntityReparentOp"/> or the last component of a <see cref="CompoundOp"/>
/// (the only place a reparent may stand, K6-35 D6: a death ends with the reparent of the player). Every place that recognises a reparent
/// (replication, mirrors) uses this; a recipient other than the owner is sent the extracted reparent, never the whole compound.
/// </summary>
public static class OperationReparent
{
    /// <summary>Returns the reparent of an operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="reparent">The reparent when found.</param>
    /// <returns>True when the operation is, or ends with, an entity reparent.</returns>
    public static bool TryGetReparent(Operation operation, [NotNullWhen(true)] out EntityReparentOp? reparent)
    {
        if (operation is EntityReparentOp top)
        {
            reparent = top;
            return true;
        }

        if (operation is CompoundOp compound && compound.Components.Count > 0 && compound.Components[^1] is EntityReparentOp last)
        {
            reparent = last;
            return true;
        }

        reparent = null;
        return false;
    }
}

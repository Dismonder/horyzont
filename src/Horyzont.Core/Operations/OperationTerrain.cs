using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Operations;

/// <summary>
/// Finds the terrain edit of an operation: a top-level <see cref="TerrainEditOp"/> or the last component of a <see cref="CompoundOp"/>
/// (the only place format 1 allows it). Every place that recognises terrain edits (replay, terrain semantics) uses this.
/// </summary>
public static class OperationTerrain
{
    /// <summary>Returns the terrain edit of an operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="edit">The terrain edit when found.</param>
    /// <returns>True when the operation edits the terrain.</returns>
    public static bool TryGetTerrainEdit(Operation operation, [NotNullWhen(true)] out TerrainEditOp? edit)
    {
        if (operation is TerrainEditOp top)
        {
            edit = top;
            return true;
        }

        if (operation is CompoundOp compound && compound.Components.Count > 0 && compound.Components[^1] is TerrainEditOp last)
        {
            edit = last;
            return true;
        }

        edit = null;
        return false;
    }
}

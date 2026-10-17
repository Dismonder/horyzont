using System.Diagnostics.CodeAnalysis;

namespace Horyzont.Core.Operations;

/// <summary>
/// Finds the part of an operation that concerns a construct: the operation itself, the last construct component of a <see cref="CompoundOp"/>
/// or a compound of its function state components. Every place that recognises construct operations uses this (K6-30 D14).
/// </summary>
public static class OperationConstructs
{
    /// <summary>Returns the construct part of an operation.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="part">The part when found.</param>
    /// <returns>True when the operation changes a construct.</returns>
    public static bool TryGetConstructPart(Operation operation, [NotNullWhen(true)] out Operation? part)
    {
        ArgumentNullException.ThrowIfNull(operation);
        switch (operation)
        {
            case ConstructCreateOp or BlockSetOp or BlockClearOp or ShipModeSetOp or BlockFunctionStateSetOp:
                part = operation;
                return true;
            case CompoundOp compound:
                return TryGetCompoundPart(compound, out part);
            default:
                part = null;
                return false;
        }
    }

    private static bool TryGetCompoundPart(CompoundOp compound, [NotNullWhen(true)] out Operation? part)
    {
        var components = compound.Components;
        if (components.Count == 0)
        {
            part = null;
            return false;
        }

        // A construct edit is the last component of a compound (D12 of K6-30); a compound with function states has no construct edit.
        if (components[^1] is ConstructCreateOp or BlockSetOp or BlockClearOp)
        {
            part = components[^1];
            return true;
        }

        var states = 0;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is BlockFunctionStateSetOp)
            {
                states++;
            }
        }

        if (states == 0)
        {
            part = null;
            return false;
        }

        if (states == components.Count)
        {
            part = compound;
            return true;
        }

        var only = new Operation[states];
        var written = 0;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is BlockFunctionStateSetOp)
            {
                only[written++] = components[i];
            }
        }

        part = new CompoundOp(only);
        return true;
    }
}

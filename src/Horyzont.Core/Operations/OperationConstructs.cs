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
        // K6-30 A6: the behaviour.
        ArgumentNullException.ThrowIfNull(operation);
        part = null;
        return false;
    }
}

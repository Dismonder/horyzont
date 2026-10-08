namespace Horyzont.Core.Ids;

/// <summary>Number of an applied operation in the journal, consecutive from 1.</summary>
/// <param name="Value">Numeric value; 0 means no operation.</param>
public readonly record struct OpId(ulong Value) : IComparable<OpId>
{
    /// <summary>No operation (0).</summary>
    public static OpId None => default;

    /// <summary>The following operation number.</summary>
    public OpId Next => new(Value + 1);

    /// <summary>The numeric value; records would otherwise print <c>Next</c> recursively.</summary>
    /// <returns>The value as invariant text.</returns>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(OpId other) => Value.CompareTo(other.Value);

    /// <summary>Less than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(OpId left, OpId right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(OpId left, OpId right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(OpId left, OpId right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(OpId left, OpId right) => left.CompareTo(right) >= 0;
}

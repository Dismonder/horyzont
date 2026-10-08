namespace Horyzont.Core.Ids;

/// <summary>Simulation time in server ticks.</summary>
/// <param name="Value">Number of ticks since the world was created.</param>
public readonly record struct Tick(ulong Value) : IComparable<Tick>
{
    /// <summary>The following tick.</summary>
    public Tick Next => new(Value + 1);

    /// <summary>The numeric value; records would otherwise print <c>Next</c> recursively.</summary>
    /// <returns>The value as invariant text.</returns>
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public int CompareTo(Tick other) => Value.CompareTo(other.Value);

    /// <summary>Less than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <(Tick left, Tick right) => left.CompareTo(right) < 0;

    /// <summary>Less than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator <=(Tick left, Tick right) => left.CompareTo(right) <= 0;

    /// <summary>Greater than.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >(Tick left, Tick right) => left.CompareTo(right) > 0;

    /// <summary>Greater than or equal.</summary>
    /// <param name="left">Left value.</param>
    /// <param name="right">Right value.</param>
    /// <returns>The comparison result.</returns>
    public static bool operator >=(Tick left, Tick right) => left.CompareTo(right) >= 0;
}

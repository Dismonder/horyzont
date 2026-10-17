namespace Horyzont.Core.Simulation;

/// <summary>Arithmetic of the mining yield and the fill cost (K6-29W D10, D12). Pure; used only by intent resolution.</summary>
internal static class MiningYield
{
    /// <summary>Tolerance against rounding of sums such as 0.1 + 0.2: a value this close to an integer counts as that integer.</summary>
    private const double Epsilon = 1e-9;

    /// <summary>Upper bound of items in one resolution, far above anything a tool sphere yields; keeps the conversions safe.</summary>
    private const int MaxItems = 1_000_000;

    /// <summary>Items in <paramref name="voxels"/> voxels of <paramref name="voxelVolume"/> m³ of a material yielding <paramref name="itemsPerCubicMeter"/>.</summary>
    internal static double Amount(int voxels, double voxelVolume, double itemsPerCubicMeter) => voxels * voxelVolume * itemsPerCubicMeter;

    /// <summary>Splits a yield into whole items and the fractional remainder that waits for the next dig.</summary>
    /// <param name="amount">Yield including the previous remainder.</param>
    /// <param name="remainder">The fractional part, 0 ≤ remainder &lt; 1.</param>
    /// <returns>The whole items.</returns>
    internal static int WholeItems(double amount, out double remainder)
    {
        if (!(amount > 0))
        {
            remainder = 0;
            return 0;
        }

        var whole = Math.Floor(amount + Epsilon);
        remainder = Math.Max(0, amount - whole);
        return (int)Math.Min(whole, MaxItems);
    }

    /// <summary>Items a fill consumes: the need rounded up; the overpayment becomes the credit for the next fill.</summary>
    /// <param name="need">Items the filled volume is worth minus the previous credit.</param>
    /// <param name="credit">New credit, at least 0.</param>
    /// <returns>Items to consume.</returns>
    internal static int Cost(double need, out double credit)
    {
        if (!(need > Epsilon))
        {
            credit = Math.Max(0, -need);
            return 0;
        }

        var cost = Math.Ceiling(need - Epsilon);
        credit = Math.Max(0, cost - need);
        return (int)Math.Min(cost, MaxItems);
    }
}

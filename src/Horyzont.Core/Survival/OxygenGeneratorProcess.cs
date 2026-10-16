using Horyzont.Core.Ids;
using Horyzont.Core.Items;

namespace Horyzont.Core.Survival;

/// <summary>The burning of fuel cells by an oxygen generator as pure functions of ticks (K6-34, D12); the settlement does not depend on how often it is called.</summary>
public static class OxygenGeneratorProcess
{
    /// <summary>Counts the fuel cells in the input container.</summary>
    /// <param name="input">The slots of the input.</param>
    /// <param name="fuel">The type of the fuel cell.</param>
    /// <returns>The number of cells.</returns>
    public static int CellsIn(ReadOnlySpan<ItemStack> input, ItemTypeId fuel) => throw new NotImplementedException("K6-34 A6");

    /// <summary>
    /// Settles the generator at <paramref name="now"/>: an idle generator (<paramref name="started"/> null) with cells starts now and burns nothing; a running one burns
    /// <c>min(cells, (now - started) / ticksPerCell)</c> whole cells (0 when <paramref name="now"/> is before the start) and continues from <c>started + burned * ticksPerCell</c> while cells are left.
    /// A cell that is burning stays in the input until it is whole burned.
    /// </summary>
    /// <param name="cells">Cells in the input.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell started at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The cells burned and the next start.</returns>
    public static GeneratorSettlement Settle(int cells, ulong ticksPerCell, Tick? started, Tick now) => throw new NotImplementedException("K6-34 A6");

    /// <summary>Whether the generator protects at <paramref name="now"/>: it was started and <c>now &lt; started + cells * ticksPerCell</c> (saturating arithmetic).</summary>
    /// <param name="cells">Cells in the input.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell started at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>True when the generator runs.</returns>
    public static bool IsRunning(int cells, ulong ticksPerCell, Tick? started, Tick now) => throw new NotImplementedException("K6-34 A6");

    /// <summary>Ticks until the last cell is burned; 0 when the generator does not run.</summary>
    /// <param name="cells">Cells in the input.</param>
    /// <param name="ticksPerCell">Ticks one cell burns.</param>
    /// <param name="started">The tick the current cell started at, null when idle.</param>
    /// <param name="now">The current tick.</param>
    /// <returns>The remaining ticks.</returns>
    public static ulong RemainingTicks(int cells, ulong ticksPerCell, Tick? started, Tick now) => throw new NotImplementedException("K6-34 A6");
}
